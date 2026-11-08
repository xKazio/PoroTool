using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PoroTool
{
    /// <summary>
    /// Ragequeue: one click creates a lobby of the chosen queue, starts
    /// matchmaking and instantly accepts the ready check; auto pick/ban then
    /// hover, lock and ban the configured champions in champ select. The
    /// pick/ban automation runs off the champ-select websocket events, so it
    /// works in every champ select while enabled - not just ragequeued ones.
    /// </summary>
    public partial class MainForm
    {
        private volatile bool ragequeueActive;
        private int ragequeueArmedAt;

        /// <summary>True while an auto-dodge is waiting out the penalty timer
        /// to re-queue, so the leave-the-queue disarm logic stays quiet.</summary>
        private volatile bool requeuePending;

        private CheckBox autoDodgeCheckBox;
        private volatile bool suppressAutoDodgeEvent;

        private readonly object pickBanLock = new object();
        private readonly Dictionary<string, int> pickBanAttempted = new Dictionary<string, int>();
        private readonly HashSet<long> pickBanDone = new HashSet<long>();

        // Session updates arrive faster than the requests they trigger. Only
        // the newest one matters, so it is parked here and a single worker
        // drains it - an update is never dropped just because the previous
        // one was still being handled (which used to skip the moment a turn
        // opened and leave the pick/ban hanging).
        private JsonObject pendingPickBanSession;
        private int pickBanWorkerRunning;

        private static readonly (string Label, string Value)[] Roles =
        {
            ("Top", "TOP"), ("Jungle", "JUNGLE"), ("Mid", "MIDDLE"),
            ("Bot", "BOTTOM"), ("Support", "UTILITY"), ("Fill", "FILL"),
        };

        // Shown until the real queue list is loaded from the client.
        private static readonly QueueChoice[] FallbackQueues =
        {
            new QueueChoice("Ranked Solo/Duo", 420, roles: true),
            new QueueChoice("Ranked Flex", 440, roles: true),
            new QueueChoice("Normal (Draft)", 400, roles: true),
            new QueueChoice("Swiftplay", 480, roles: false),
            new QueueChoice("ARAM", 450, roles: false),
        };

        private sealed class QueueChoice
        {
            public string Label { get; }
            public int Id { get; }
            public bool Roles { get; }

            public QueueChoice(string label, int id, bool roles)
            {
                Label = label;
                Id = id;
                Roles = roles;
            }

            public override string ToString() => Label;
        }

        private sealed class ChampChoice
        {
            public string Name { get; }
            public long Id { get; }

            public ChampChoice(string name, long id)
            {
                Name = name;
                Id = id;
            }

            public override string ToString() => Name;
        }

        private void InitRagequeue()
        {
            league.GameflowPhaseChanged += OnRagequeuePhase;
            league.OnDisconnected += () => OnRagequeuePhase("None");
            league.ChampSelectSessionChanged += OnRagequeueChampSelect;
        }

        // ---------- Armed-state handling ----------

        private void OnRagequeuePhase(string phase)
        {
            if (phase != "ChampSelect")
                lock (pickBanLock) { pickBanAttempted.Clear(); pickBanDone.Clear(); }

            if (!ragequeueActive) return;

            if (phase == "ReadyCheck")
            {
                // Instant accept, independent of the auto-accept setting/delay.
                Task.Run(async () =>
                {
                    try { await league.Post("/lol-matchmaking/v1/ready-check/accept", "{}"); }
                    catch { }
                });
            }
            else if (phase == "InProgress" || phase == "None" || phase == "EndOfGame")
            {
                ragequeueActive = false;
            }
            else if (phase == "Lobby" && !requeuePending && Environment.TickCount - ragequeueArmedAt > 3000)
            {
                // Back in the lobby without a game start = the queue was left
                // on purpose; stop force-accepting.
                ragequeueActive = false;
                SetStatusSafe("Ragequeue disarmed (left the queue).", StatusKind.Info);
            }
        }

        // ---------- Auto pick / ban ----------

        private void OnRagequeueChampSelect(JsonObject session)
        {
            if (session == null) return;
            if (!liveSettings.AutoPick && !liveSettings.AutoBan && !liveSettings.AutoDodge) return;

            Interlocked.Exchange(ref pendingPickBanSession, session);
            if (Interlocked.CompareExchange(ref pickBanWorkerRunning, 1, 0) != 0) return;   // the running worker picks it up

            Task.Run(async () =>
            {
                while (true)
                {
                    var next = Interlocked.Exchange(ref pendingPickBanSession, null);
                    if (next != null)
                    {
                        try { await DoAutoPickBan(next); }
                        catch (Exception ex) { Log.Write("auto pick/ban: " + ex.Message); }
                        continue;
                    }

                    Interlocked.Exchange(ref pickBanWorkerRunning, 0);
                    // An update may have slipped in between the last check and
                    // the release; take the worker back if nobody else did.
                    if (Volatile.Read(ref pendingPickBanSession) == null ||
                        Interlocked.CompareExchange(ref pickBanWorkerRunning, 1, 0) != 0)
                        return;
                }
            });
        }

        private async Task DoAutoPickBan(JsonObject session)
        {
            long myCell = LongOf(session, "localPlayerCellId");

            string phase = "";
            if (session.TryGetValue("timer", out var timerValue) && timerValue is JsonObject timer)
                phase = StringOf(timer, "phase");

            if (!(session.TryGetValue("actions", out var actionsValue) && actionsValue is JsonArray rounds)) return;

            if (await TryAutoDodge(rounds, myCell, LongOf(session, "queueId"))) return;

            foreach (var roundValue in rounds)
            {
                if (!(roundValue is JsonArray round)) continue;
                foreach (var actionValue in round)
                {
                    if (!(actionValue is JsonObject action)) continue;
                    if (LongOf(action, "actorCellId") != myCell) continue;
                    if (BoolOf(action, "completed")) continue;

                    long actionId = LongOf(action, "id");
                    string type = StringOf(action, "type");
                    bool inProgress = BoolOf(action, "isInProgress");
                    lock (pickBanLock) { if (pickBanDone.Contains(actionId)) continue; }

                    // Locking and banning get several tries: the client rejects
                    // them for a moment right as the turn opens, and a single
                    // shot would silently give up.
                    if (type == "pick" && liveSettings.AutoPick && liveSettings.PickChampionId > 0)
                    {
                        if (inProgress && Attempt(actionId + ":lock", 4))
                            await CompleteChampSelectAction(actionId, liveSettings.PickChampionId, "pick");
                        else if (phase == "PLANNING" && LongOf(action, "championId") != liveSettings.PickChampionId &&
                                 Attempt(actionId + ":hover", 3))
                            await HoverChampSelectAction(actionId, liveSettings.PickChampionId);
                    }
                    else if (type == "ban" && liveSettings.AutoBan && liveSettings.BanChampionId > 0 &&
                             inProgress && Attempt(actionId + ":ban", 4))
                    {
                        await CompleteChampSelectAction(actionId, liveSettings.BanChampionId, "ban");
                    }
                }
            }
        }

        /// <summary>Reflects the auto-dodge flag on screen after it disarms itself.</summary>
        private void UpdateAutoDodgeUi()
        {
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)UpdateAutoDodgeUi); } catch { }
                return;
            }

            var box = autoDodgeCheckBox;
            if (box == null || box.IsDisposed || box.Checked == liveSettings.AutoDodge) return;

            suppressAutoDodgeEvent = true;
            box.Checked = liveSettings.AutoDodge;
            suppressAutoDodgeEvent = false;
        }

        /// <summary>
        /// Rate-limits an action to <paramref name="maxTries"/> attempts per champ
        /// select, so a transient failure can be retried on the next session
        /// update without ever turning into request spam.
        /// </summary>
        private bool Attempt(string key, int maxTries = 1)
        {
            lock (pickBanLock)
            {
                pickBanAttempted.TryGetValue(key, out int tries);
                if (tries >= maxTries) return false;
                pickBanAttempted[key] = tries + 1;
                return true;
            }
        }

        /// <summary>
        /// Dodges (and later re-queues) when anyone else has locked in one of
        /// the champions on the dodge list. Returns true when the dodge fired.
        /// </summary>
        private async Task<bool> TryAutoDodge(JsonArray rounds, long myCell, long queueId)
        {
            if (!liveSettings.AutoDodge || liveSettings.DodgeChampionIds.Count == 0) return false;

            foreach (var roundValue in rounds)
            {
                if (!(roundValue is JsonArray round)) continue;
                foreach (var actionValue in round)
                {
                    if (!(actionValue is JsonObject action)) continue;
                    if (StringOf(action, "type") != "pick") continue;
                    if (!BoolOf(action, "completed")) continue;
                    if (LongOf(action, "actorCellId") == myCell) continue;

                    long championId = LongOf(action, "championId");
                    if (championId <= 0 || !liveSettings.DodgeChampionIds.Contains(championId)) continue;
                    if (!Attempt("dodge")) return true;   // already dodging this champ select

                    string name = await ChampionNameById(championId);
                    SetStatusSafe(name + " was locked - dodging...", StatusKind.Info);
                    requeuePending = true;
                    ragequeueActive = false;

                    // "Dodge only once" stops after a single dodge instead of
                    // dodging every champ select that matches.
                    bool once = liveSettings.DodgeOnce;
                    if (once)
                    {
                        liveSettings.AutoDodge = false;
                        liveSettings.Save();
                        UpdateAutoDodgeUi();
                    }

                    var outcome = await DodgeAsync(allowRestart: true, gracefulMs: await GracefulCloseMsAsync());
                    if (!Dodged(outcome))
                    {
                        requeuePending = false;
                        SetStatusSafe(DodgeOutcomeText(outcome, "Auto-dodge of " + name), DodgeOutcomeKind(outcome));
                        return true;
                    }

                    if (!once) _ = Task.Run(() => RequeueAfterDodgeAsync(queueId));
                    else
                    {
                        requeuePending = false;
                        SetStatusSafe(DodgeOutcomeText(outcome, "Dodged " + name) + " Auto-dodge turned itself off (dodge only once).",
                                      DodgeOutcomeKind(outcome));
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// After an auto-dodge: get back into the configured queue as soon as
        /// the dodge penalty allows. The penalty length is read from the
        /// matchmaking search-state errors, so the re-queue fires the moment
        /// the timer runs out. A restart-dodge takes the client away for
        /// 15-30 s and it then needs a moment to log back in, so failures in
        /// that window are retried instead of giving up. Unticking auto-dodge
        /// cancels the wait.
        /// </summary>
        private async Task RequeueAfterDodgeAsync(long dodgedQueueId)
        {
            // Back into the queue that was dodged, not whatever the Ragequeue
            // view has selected (a restart-dodge loses the lobby).
            long queueId = dodgedQueueId > 0 ? dodgedQueueId : liveSettings.QueueId;
            try
            {
                var giveUpAt = DateTime.UtcNow.AddMinutes(3);
                string lastProblem = "";

                while (DateTime.UtcNow < giveUpAt)
                {
                    await Task.Delay(3000);
                    if (!liveSettings.AutoDodge) return;

                    if (!league.IsConnected)
                    {
                        SetStatusSafe("Dodged - waiting for League to come back to re-queue...", StatusKind.Info);
                        continue;
                    }

                    // Only keep going while sitting in (or just out of) a lobby;
                    // anything else means the player moved on.
                    if (!(await league.Get("/lol-gameflow/v1/gameflow-phase") is string phase)) continue;
                    if (phase == "Matchmaking" || phase == "ReadyCheck" || phase == "ChampSelect" || phase == "InProgress") return;
                    if (phase != "Lobby" && phase != "None") continue;

                    if (await league.Get("/lol-lobby/v2/lobby") == null)
                    {
                        var (lobbyStatus, lobbyContent) = await league.Request("POST", "/lol-lobby/v2/lobby",
                            "{\"queueId\": " + queueId + "}");
                        if (lobbyStatus < 200 || lobbyStatus >= 300)
                        {
                            lastProblem = "could not recreate the lobby (HTTP " + lobbyStatus + ErrorDetail(lobbyContent) + ")";
                            continue;   // usually the client is still logging in
                        }
                        // Positional queues need roles; harmless elsewhere.
                        await league.Put("/lol-lobby/v2/lobby/members/localMember/position-preferences",
                            "{\"firstPreference\": \"" + liveSettings.RoleFirst + "\", \"secondPreference\": \"" + liveSettings.RoleSecond + "\"}");
                        await Task.Delay(300);
                    }

                    await league.Request("POST", "/lol-lobby/v2/lobby/matchmaking/search", "{}");
                    await Task.Delay(1000);

                    var state = await league.Get("/lol-lobby/v2/lobby/matchmaking/search-state") as JsonObject;
                    if (StringOf(state, "searchState") == "Searching")
                    {
                        ragequeueArmedAt = Environment.TickCount;
                        ragequeueActive = true;
                        SetStatusSafe("Dodged and back in the queue.", StatusKind.Success);
                        return;
                    }

                    double penalty = PenaltySecondsOf(state);
                    if (penalty <= 0)
                    {
                        lastProblem = "the queue did not start";
                        continue;   // transient state; try again in a few seconds
                    }

                    // Sit out the penalty, then loop around and search again.
                    var until = DateTime.UtcNow.AddSeconds(penalty + 1);
                    giveUpAt = until.AddMinutes(1);
                    while (true)
                    {
                        var left = until - DateTime.UtcNow;
                        if (left <= TimeSpan.Zero) break;
                        if (!liveSettings.AutoDodge) return;

                        SetStatusSafe("Dodge timer: " + left.ToString(left.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss") +
                                      " - re-queueing automatically.", StatusKind.Info);
                        await Task.Delay(left > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : left);
                    }
                }

                SetStatusSafe("Re-queue failed" + (lastProblem.Length > 0 ? ": " + lastProblem : "") + " - start the queue manually.", StatusKind.Error);
            }
            catch (Exception ex)
            {
                // Client closed mid-wait, or a request failed outright.
                Log.Write("re-queue after dodge: " + ex.Message);
            }
            finally
            {
                requeuePending = false;
            }
        }

        private static double PenaltySecondsOf(JsonObject searchState)
        {
            if (searchState == null || !searchState.TryGetValue("errors", out var errorsValue) ||
                !(errorsValue is JsonArray errors)) return 0;

            double max = 0;
            foreach (var errorValue in errors)
            {
                if (!(errorValue is JsonObject error)) continue;
                if (error.TryGetValue("penaltyTimeRemaining", out var remaining) && remaining != null)
                {
                    try { max = Math.Max(max, Convert.ToDouble(remaining)); } catch { }
                }
            }
            return max;
        }

        /// <summary>Hovers a champion on the action (the pick intent in planning).</summary>
        private async Task HoverChampSelectAction(long actionId, long championId)
        {
            var (status, content) = await league.Request("PATCH", "/lol-champ-select/v1/session/actions/" + actionId,
                                                        "{\"championId\": " + championId + "}");
            Log.Write("hover action " + actionId + " champion " + championId + " -> HTTP " + status + " " + Log.Snippet(content));
        }

        /// <summary>
        /// Locks in a pick or a ban: one PATCH with completed=true - exactly
        /// what the client's own Lock In / Ban button sends. (POST .../complete
        /// answers 204 but leaves the action open; trusting that 204 is what
        /// left picks and bans stuck on hover.) Success is only reported once
        /// the session really shows the action completed.
        /// </summary>
        private async Task CompleteChampSelectAction(long actionId, long championId, string verb)
        {
            string url = "/lol-champ-select/v1/session/actions/" + actionId;
            string body = "{\"championId\": " + championId + ", \"completed\": true}";
            int status = 0;
            string content = "";
            var result = (Found: true, Completed: false, ChampionId: 0L);

            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0) await Task.Delay(400);

                (status, content) = await league.Request("PATCH", url, body);
                Log.Write(verb + " action " + actionId + " champion " + championId + " (try " + (attempt + 1) + ") -> HTTP " +
                          status + " " + Log.Snippet(content));

                // The session takes a moment to reflect the lock.
                for (int check = 0; check < 4; check++)
                {
                    await Task.Delay(150);
                    result = await ChampSelectActionStateAsync(actionId);
                    if (result.Completed || !result.Found) break;
                }
                if (result.Completed || !result.Found) break;
            }

            if (result.Completed)
            {
                lock (pickBanLock) pickBanDone.Add(actionId);
                string name = await ChampionNameById(result.ChampionId > 0 ? result.ChampionId : championId);
                SetStatusSafe((verb == "ban" ? "Banned " : "Locked in ") + name + ".", StatusKind.Success);
                return;
            }

            string wanted = await ChampionNameById(championId);
            SetStatusSafe("Auto-" + verb + " of " + wanted + " failed (HTTP " + status + ErrorDetail(content) +
                          (result.Found ? "" : ", the turn already ended") + ") - do it manually.", StatusKind.Error);
        }

        /// <summary>Reads one action back from a fresh champ-select session.</summary>
        private async Task<(bool Found, bool Completed, long ChampionId)> ChampSelectActionStateAsync(long actionId)
        {
            try
            {
                if (!(await league.Get("/lol-champ-select/v1/session") is JsonObject session) ||
                    !(session.TryGetValue("actions", out var av) && av is JsonArray rounds))
                    return (false, false, 0);

                foreach (var roundValue in rounds)
                {
                    if (!(roundValue is JsonArray round)) continue;
                    foreach (var actionValue in round)
                    {
                        if (actionValue is JsonObject action && LongOf(action, "id") == actionId)
                            return (true, BoolOf(action, "completed"), LongOf(action, "championId"));
                    }
                }
            }
            catch { }
            return (false, false, 0);
        }

        private static string ErrorDetail(string content)
        {
            try
            {
                if (SimpleJson.DeserializeObject(content) is JsonObject error)
                {
                    string message = StringOf(error, "message");
                    if (message.Length > 0)
                        return ", " + (message.Length > 120 ? message.Substring(0, 120) : message);
                }
            }
            catch { }
            return "";
        }

        private static async Task<string> ChampionNameById(long championId)
        {
            try
            {
                var champions = await DataDragon.GetChampionsAsync();
                return champions.FirstOrDefault(c => c.Key == championId)?.Name ?? "champion " + championId;
            }
            catch
            {
                return "champion " + championId;
            }
        }

        // ---------- Starting the queue ----------

        private async Task RagequeueNowAsync(QueueChoice queue)
        {
            if (!EnsureConnected()) return;

            try
            {
                SetStatus("Creating a " + queue.Label + " lobby...");
                var (lobbyStatus, _) = await league.Request("POST", "/lol-lobby/v2/lobby", "{\"queueId\": " + queue.Id + "}");
                if (lobbyStatus < 200 || lobbyStatus >= 300)
                {
                    SetStatus("Creating the lobby failed (HTTP " + lobbyStatus + ").", StatusKind.Error);
                    return;
                }

                if (queue.Roles)
                {
                    string roles = "{\"firstPreference\": \"" + liveSettings.RoleFirst +
                                   "\", \"secondPreference\": \"" + liveSettings.RoleSecond + "\"}";
                    await league.Put("/lol-lobby/v2/lobby/members/localMember/position-preferences", roles);
                }

                await Task.Delay(300);   // let the lobby settle before searching

                var (searchStatus, searchContent) = await league.Request("POST", "/lol-lobby/v2/lobby/matchmaking/search", "{}");
                if (searchStatus < 200 || searchStatus >= 300)
                {
                    string detail = "";
                    if (SimpleJson.DeserializeObject(searchContent) is JsonObject error)
                        detail = " - " + StringOf(error, "message");
                    SetStatus("Starting the queue failed (HTTP " + searchStatus + detail + ").", StatusKind.Error);
                    return;
                }

                ragequeueArmedAt = Environment.TickCount;
                ragequeueActive = true;
                SetStatus("Ragequeue armed: in the " + queue.Label + " queue. Ready check will be accepted instantly.", StatusKind.Success);
            }
            catch (Exception ex)
            {
                SetStatus("Ragequeue failed: " + ex.Message, StatusKind.Error);
            }
        }

        // ---------- View ----------

        private void ragequeueButton_Click(object sender, EventArgs e)
        {
            ShowFeatureView(BuildRagequeueView, requireConnection: false);
        }

        private void BuildRagequeueView(Panel panel)
        {
            var token = viewCts.Token;

            var layout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };

            layout.Controls.Add(MakeHeading("Ragequeue"));
            layout.Controls.Add(MakeMutedLabel("One click: creates the lobby, queues up and instantly accepts the ready check. Auto pick and ban work in every champ select while enabled.", 460));

            var queueBox = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 0, 8) };
            Theme.StyleComboBox(queueBox);

            var firstRoleBox = new ComboBox { Width = 106, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 8, 0) };
            Theme.StyleComboBox(firstRoleBox);
            var secondRoleBox = new ComboBox { Width = 106, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0) };
            Theme.StyleComboBox(secondRoleBox);
            foreach (var role in Roles)
            {
                firstRoleBox.Items.Add(new Choice(role.Label, role.Value));
                secondRoleBox.Items.Add(new Choice(role.Label, role.Value));
            }
            SelectChoice(firstRoleBox, liveSettings.RoleFirst);
            SelectChoice(secondRoleBox, liveSettings.RoleSecond);

            var rolesRow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 8)
            };
            rolesRow.Controls.Add(firstRoleBox);
            rolesRow.Controls.Add(secondRoleBox);

            var pickBox = MakeChampionBox();
            var banBox = MakeChampionBox();

            var autoPickCheck = new CheckBox { Text = "Auto pick && lock", Checked = liveSettings.AutoPick, Margin = new Padding(0, 2, 0, 2) };
            Theme.StyleCheckBox(autoPickCheck);
            autoPickCheck.CheckedChanged += (s, e) => { liveSettings.AutoPick = autoPickCheck.Checked; liveSettings.Save(); };

            var autoBanCheck = new CheckBox { Text = "Auto ban", Checked = liveSettings.AutoBan, Margin = new Padding(0, 2, 0, 12) };
            Theme.StyleCheckBox(autoBanCheck);
            autoBanCheck.CheckedChanged += (s, e) => { liveSettings.AutoBan = autoBanCheck.Checked; liveSettings.Save(); };

            // Dodge list: champions that trigger an auto-dodge when locked.
            var dodgeBox = MakeChampionBox();
            dodgeBox.Margin = new Padding(0, 0, 8, 0);

            var addDodgeButton = new Button { Text = "Add", Size = new Size(70, 27), Margin = new Padding(0, 0, 8, 0) };
            Theme.StyleButton(addDodgeButton);
            addDodgeButton.TextAlign = ContentAlignment.MiddleCenter;
            addDodgeButton.Padding = Padding.Empty;

            var removeDodgeButton = new Button { Text = "Remove", Size = new Size(70, 27), Margin = new Padding(0) };
            Theme.StyleButton(removeDodgeButton);
            removeDodgeButton.TextAlign = ContentAlignment.MiddleCenter;
            removeDodgeButton.Padding = Padding.Empty;

            var dodgeRow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 4)
            };
            dodgeRow.Controls.Add(dodgeBox);
            dodgeRow.Controls.Add(addDodgeButton);
            dodgeRow.Controls.Add(removeDodgeButton);

            var dodgeListBox = new ListBox
            {
                Width = 220,
                Height = 76,
                Font = Theme.RowFont,
                BackColor = Theme.Surface,
                ForeColor = Theme.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(0, 0, 0, 8)
            };

            var autoDodgeCheck = new CheckBox
            {
                Text = "Auto dodge when one of these is locked, then re-queue after the dodge timer",
                Checked = liveSettings.AutoDodge,
                Margin = new Padding(0, 2, 0, 12),
                AutoSize = false,
                Width = 460,
                Height = 20
            };
            Theme.StyleCheckBox(autoDodgeCheck);
            autoDodgeCheck.AutoSize = true;
            autoDodgeCheck.CheckedChanged += (s, e) =>
            {
                if (suppressAutoDodgeEvent) return;
                liveSettings.AutoDodge = autoDodgeCheck.Checked;
                liveSettings.Save();
            };
            autoDodgeCheckBox = autoDodgeCheck;

            var dodgeOnceCheck = new CheckBox
            {
                Text = "Dodge only once (turn auto dodge off after the first dodge)",
                Checked = liveSettings.DodgeOnce,
                Margin = new Padding(0, 0, 0, 12),
                AutoSize = true
            };
            Theme.StyleCheckBox(dodgeOnceCheck);
            dodgeOnceCheck.CheckedChanged += (s, e) => { liveSettings.DodgeOnce = dodgeOnceCheck.Checked; liveSettings.Save(); };

            addDodgeButton.Click += (s, e) =>
            {
                string text = dodgeBox.Text.Trim();
                var choice = dodgeBox.SelectedItem as ChampChoice;
                if (choice == null || !string.Equals(choice.Name, text, StringComparison.OrdinalIgnoreCase))
                    choice = dodgeBox.Items.OfType<ChampChoice>()
                        .FirstOrDefault(c => string.Equals(c.Name, text, StringComparison.OrdinalIgnoreCase));
                if (choice == null || choice.Id <= 0) return;
                if (liveSettings.DodgeChampionIds.Contains(choice.Id)) return;

                liveSettings.DodgeChampionIds.Add(choice.Id);
                liveSettings.Save();
                dodgeListBox.Items.Add(choice);
            };

            removeDodgeButton.Click += (s, e) =>
            {
                if (!(dodgeListBox.SelectedItem is ChampChoice choice)) return;
                liveSettings.DodgeChampionIds.Remove(choice.Id);
                liveSettings.Save();
                dodgeListBox.Items.Remove(choice);
            };

            var goButton = new Button { Text = "Ragequeue now", Size = new Size(220, 40), Margin = new Padding(0, 0, 0, 8) };
            Theme.StylePrimaryButton(goButton);
            goButton.Click += async (s, e) =>
            {
                if (!(queueBox.SelectedItem is QueueChoice queue)) return;
                goButton.Enabled = false;
                try { await RagequeueNowAsync(queue); }
                finally { if (!goButton.IsDisposed) goButton.Enabled = true; }
            };

            queueBox.SelectedIndexChanged += (s, e) =>
            {
                if (!(queueBox.SelectedItem is QueueChoice queue)) return;
                firstRoleBox.Enabled = secondRoleBox.Enabled = queue.Roles;
                liveSettings.QueueId = queue.Id;
                liveSettings.Save();
            };
            firstRoleBox.SelectedIndexChanged += (s, e) =>
            {
                if (firstRoleBox.SelectedItem is Choice c) { liveSettings.RoleFirst = c.Value; liveSettings.Save(); }
            };
            secondRoleBox.SelectedIndexChanged += (s, e) =>
            {
                if (secondRoleBox.SelectedItem is Choice c) { liveSettings.RoleSecond = c.Value; liveSettings.Save(); }
            };

            layout.Controls.Add(MakeLabeled("Queue", queueBox));
            layout.Controls.Add(MakeLabeled("Roles (primary / secondary)", rolesRow));
            layout.Controls.Add(MakeLabeled("Champion to pick", pickBox));
            layout.Controls.Add(MakeLabeled("Champion to ban", banBox));
            layout.Controls.Add(autoPickCheck);
            layout.Controls.Add(autoBanCheck);
            layout.Controls.Add(MakeLabeled("Dodge list", dodgeRow));
            layout.Controls.Add(dodgeListBox);
            layout.Controls.Add(autoDodgeCheck);
            layout.Controls.Add(dodgeOnceCheck);
            layout.Controls.Add(goButton);
            layout.Controls.Add(MakeMutedLabel("Normal queue rules still apply (dodge timers, autofill). ARAM and Swiftplay have no pick phase, so auto pick/ban/dodge simply do nothing there. " +
                                               "Riot no longer lets apps leave ranked/normal champ select, so an auto-dodge there closes League and starts it again before re-queueing.", 460));
            panel.Controls.Add(layout);

            FillQueueBox(queueBox, FallbackQueues);
            _ = LoadQueuesAsync(queueBox, token);
            _ = LoadChampionBoxesAsync(pickBox, banBox, dodgeBox, dodgeListBox, token);
        }

        private ComboBox MakeChampionBox()
        {
            var box = new ComboBox
            {
                Width = 220,
                DropDownStyle = ComboBoxStyle.DropDown,
                AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                AutoCompleteSource = AutoCompleteSource.ListItems,
                Margin = new Padding(0, 0, 0, 8)
            };
            Theme.StyleComboBox(box);
            box.Items.Add(new ChampChoice("(loading...)", 0));
            box.SelectedIndex = 0;
            box.Enabled = false;
            return box;
        }

        private static void SelectChoice(ComboBox box, string value)
        {
            for (int i = 0; i < box.Items.Count; i++)
                if (box.Items[i] is Choice c && c.Value == value) { box.SelectedIndex = i; return; }
            if (box.Items.Count > 0) box.SelectedIndex = 0;
        }

        private void FillQueueBox(ComboBox queueBox, IReadOnlyList<QueueChoice> queues)
        {
            queueBox.Items.Clear();
            foreach (var queue in queues) queueBox.Items.Add(queue);

            int saved = -1;
            for (int i = 0; i < queueBox.Items.Count; i++)
                if (((QueueChoice)queueBox.Items[i]).Id == liveSettings.QueueId) { saved = i; break; }
            queueBox.SelectedIndex = saved >= 0 ? saved : 0;
        }

        private async Task LoadQueuesAsync(ComboBox queueBox, CancellationToken token)
        {
            if (!league.IsConnected) return;
            try
            {
                if (!(await league.Get("/lol-game-queues/v1/queues") is JsonArray queues)) return;

                var available = new List<QueueChoice>();
                foreach (JsonObject queue in queues)
                {
                    if (StringOf(queue, "queueAvailability") != "Available") continue;
                    if (StringOf(queue, "category") != "PvP") continue;

                    string gameMode = StringOf(queue, "gameMode");
                    string name = StringOf(queue, "name");
                    if (name.Length == 0 || gameMode == "TFT" || gameMode.StartsWith("TUTORIAL")) continue;
                    if (name.IndexOf("Clash", StringComparison.OrdinalIgnoreCase) >= 0) continue;   // needs a team

                    available.Add(new QueueChoice(name, (int)LongOf(queue, "id"), BoolOf(queue, "showPositionSelector")));
                }
                if (available.Count == 0) return;

                // The everyday queues first, rotating modes after them.
                int[] preferred = { 420, 440, 400, 480, 450 };
                available = available
                    .OrderBy(q => { int i = Array.IndexOf(preferred, q.Id); return i < 0 ? preferred.Length : i; })
                    .ThenBy(q => q.Label, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (token.IsCancellationRequested || queueBox.IsDisposed) return;
                FillQueueBox(queueBox, available);
            }
            catch
            {
                // The fallback list stays in place.
            }
        }

        private async Task LoadChampionBoxesAsync(ComboBox pickBox, ComboBox banBox, ComboBox dodgeBox, ListBox dodgeListBox, CancellationToken token)
        {
            try
            {
                var champions = await DataDragon.GetChampionsAsync();

                // Only owned champions can be locked in; fall back to the full
                // list when the inventory can't be read.
                HashSet<long> owned = null;
                try
                {
                    if (league.IsConnected &&
                        await league.Get("/lol-summoner/v1/current-summoner") is JsonObject summoner)
                    {
                        long summonerId = LongOf(summoner, "summonerId");
                        if (summonerId > 0 &&
                            await league.Get("/lol-champions/v1/inventories/" + summonerId + "/champions-minimal") is JsonArray inventory)
                        {
                            owned = new HashSet<long>();
                            foreach (JsonObject champion in inventory)
                            {
                                long id = LongOf(champion, "id");
                                if (id <= 0) continue;
                                if (champion.TryGetValue("ownership", out var ov) && ov is JsonObject ownership &&
                                    BoolOf(ownership, "owned"))
                                    owned.Add(id);
                            }
                            if (owned.Count == 0) owned = null;
                        }
                    }
                }
                catch { }

                if (token.IsCancellationRequested || pickBox.IsDisposed || banBox.IsDisposed || dodgeBox.IsDisposed) return;

                var pickable = champions.Where(c => owned == null || owned.Contains(c.Key)).ToList();
                FillChampionBox(pickBox, pickable, () => liveSettings.PickChampionId, id => liveSettings.PickChampionId = id);
                FillChampionBox(banBox, champions, () => liveSettings.BanChampionId, id => liveSettings.BanChampionId = id);

                dodgeBox.Items.Clear();
                foreach (var champion in champions)
                    dodgeBox.Items.Add(new ChampChoice(champion.Name, champion.Key));
                dodgeBox.SelectedIndex = -1;
                dodgeBox.Text = "";
                dodgeBox.Enabled = true;

                dodgeListBox.Items.Clear();
                foreach (long id in liveSettings.DodgeChampionIds)
                {
                    var entry = champions.FirstOrDefault(c => c.Key == id);
                    dodgeListBox.Items.Add(new ChampChoice(entry?.Name ?? "champion " + id, id));
                }
            }
            catch (Exception ex)
            {
                SetStatus("Loading the champion list failed: " + ex.Message, StatusKind.Error);
            }
        }

        private void FillChampionBox(ComboBox box, List<DataDragon.ChampionEntry> champions, Func<long> get, Action<long> set)
        {
            box.Items.Clear();
            box.Items.Add(new ChampChoice("(none)", 0));
            foreach (var champion in champions)
                box.Items.Add(new ChampChoice(champion.Name, champion.Key));

            int saved = 0;
            for (int i = 0; i < box.Items.Count; i++)
                if (((ChampChoice)box.Items[i]).Id == get()) { saved = i; break; }
            box.SelectedIndex = saved;
            box.Enabled = true;

            box.SelectedIndexChanged += (s, e) =>
            {
                if (box.SelectedItem is ChampChoice c) { set(c.Id); liveSettings.Save(); }
            };
            // Typing filters via autocomplete; on leave, snap the text back to
            // a real champion (or the saved one) so no garbage sticks around.
            box.Leave += (s, e) =>
            {
                string text = box.Text.Trim();
                var match = box.Items.OfType<ChampChoice>()
                    .FirstOrDefault(c => string.Equals(c.Name, text, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    if (!ReferenceEquals(box.SelectedItem, match)) box.SelectedItem = match;
                }
                else
                {
                    var current = box.Items.OfType<ChampChoice>().FirstOrDefault(c => c.Id == get());
                    box.SelectedItem = current ?? box.Items[0];
                }
            };
        }

        private static bool BoolOf(JsonObject obj, string key)
        {
            return obj != null && obj.TryGetValue(key, out var value) && true.Equals(value);
        }
    }
}
