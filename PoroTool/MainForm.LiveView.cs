using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PoroTool
{
    /// <summary>
    /// Live lobby features ported from the reveal tool: auto-accept the
    /// ready-check, reveal your champ-select teammates' Riot IDs from the local
    /// chat service, and open a multi-search page for them. Auto-accept and the
    /// reveal run in the background off the gameflow websocket events, so they
    /// work whether or not the Live view is the one on screen. Nothing is sent
    /// to any external server - the original's analytics call is intentionally
    /// dropped.
    /// </summary>
    public partial class MainForm
    {
        private LiveSettings liveSettings;

        private readonly object revealLock = new object();
        private List<string> revealedPlayers = new List<string>();
        private string revealRegion;
        private bool multiOpened;
        private CancellationTokenSource revealCts;
        private volatile string currentPhase = "";

        private bool dodgeArmed;
        private bool dodgeScheduled;
        private volatile bool suppressDodgeEvent;

        // Live view controls; null while the view is not on screen.
        private FlowLayoutPanel revealListPanel;
        private Label revealPhaseLabel;
        private CheckBox dodgeArmCheckBox;

        private void InitLive()
        {
            liveSettings = LiveSettings.Load();
            league.OnConnected += OnLiveConnected;
            league.GameflowPhaseChanged += OnGameflowPhase;
            league.ChampSelectSessionChanged += OnChampSelectSession;
        }

        private void OnLiveConnected()
        {
            // The websocket only fires on changes, so grab the current phase in
            // case the app started while already in queue or champ select.
            Task.Run(async () =>
            {
                try
                {
                    if (await league.Get("/lol-gameflow/v1/gameflow-phase") is string phase)
                        OnGameflowPhase(phase);
                }
                catch { }
            });
        }

        private void OnGameflowPhase(string phase)
        {
            currentPhase = phase ?? "";

            if (phase == "ReadyCheck") _ = AutoAcceptAsync();

            if (phase == "ChampSelect")
            {
                StartReveal();
            }
            else
            {
                StopReveal();
                ResetDodge();   // leaving champ select disarms the last-second dodge
            }

            UpdateLiveUi();
        }

        // ---------- Auto accept ----------

        private async Task AutoAcceptAsync()
        {
            if (liveSettings == null || !liveSettings.AutoAccept) return;

            try
            {
                int delay = Math.Max(0, liveSettings.AcceptDelay - 1000);
                await Task.Delay(delay);
                if (!league.IsConnected) return;

                await league.Post("/lol-matchmaking/v1/ready-check/accept", "{}");
                SetStatusSafe("Auto-accepted the match.", StatusKind.Success);
            }
            catch
            {
                // Ready-check may have ended already; nothing to do.
            }
        }

        // ---------- Reveal ----------

        private void StartReveal()
        {
            StopReveal();

            if (!league.HasRiotClient)
            {
                SetStatusSafe("Lobby reveal needs the Riot Client - try running Poro Tool as administrator.", StatusKind.Error);
                return;
            }

            revealCts = new CancellationTokenSource();
            multiOpened = false;
            var token = revealCts.Token;
            Task.Run(() => RevealLoop(token));
        }

        private void StopReveal()
        {
            revealCts?.Cancel();
            revealCts = null;
            lock (revealLock) revealedPlayers = new List<string>();
            revealRegion = null;
            UpdateLiveUi();
        }

        private async Task RevealLoop(CancellationToken token)
        {
            try
            {
                if (revealRegion == null)
                {
                    if (await league.GetRiotClient("/riotclient/region-locale") is JsonObject region &&
                        region.TryGetValue("webRegion", out var wr))
                    {
                        revealRegion = wr as string ?? "";
                        if (revealRegion == "SG2") revealRegion = "SG";   // op.gg/etc. use SG
                    }
                }

                while (!token.IsCancellationRequested)
                {
                    if (!(await league.Get("/lol-gameflow/v1/gameflow-phase") is string phase) || phase != "ChampSelect")
                        break;

                    var chat = await league.GetRiotClient("/chat/v5/participants") as JsonObject;
                    var names = ExtractChampSelectNames(chat);
                    token.ThrowIfCancellationRequested();

                    if (names != null)
                    {
                        bool changed;
                        lock (revealLock)
                        {
                            changed = !revealedPlayers.SequenceEqual(names);
                            if (changed) revealedPlayers = names;
                        }

                        if (changed)
                        {
                            UpdateLiveUi();
                            if (!multiOpened && names.Count > 0 && liveSettings.AutoOpen)
                            {
                                multiOpened = true;
                                OpenMulti();
                            }
                        }

                        if (names.Count >= 5) break;
                    }

                    await Task.Delay(2000, token);
                }
            }
            catch (OperationCanceledException) { }
            catch { }
        }

        private static List<string> ExtractChampSelectNames(JsonObject chat)
        {
            if (chat == null || !chat.TryGetValue("participants", out var pv) || !(pv is JsonArray participants))
                return null;

            var names = new List<string>();
            foreach (var item in participants)
            {
                if (!(item is JsonObject p)) continue;

                p.TryGetValue("cid", out var cidValue);
                string cid = cidValue as string ?? "";
                // The champ-select chat room isolates the current lobby's players.
                if (cid.IndexOf("champ-select", StringComparison.OrdinalIgnoreCase) < 0) continue;

                string name = FirstString(p, "game_name", "gameName");
                string tag = FirstString(p, "game_tag", "gameTag");
                if (string.IsNullOrEmpty(name)) continue;

                names.Add(string.IsNullOrEmpty(tag) ? name : name + "#" + tag);
            }
            return names;
        }

        private static string FirstString(JsonObject obj, params string[] keys)
        {
            foreach (var key in keys)
                if (obj.TryGetValue(key, out var v) && v is string s && s.Length > 0)
                    return s;
            return "";
        }

        private void OpenMulti()
        {
            List<string> names;
            lock (revealLock) names = new List<string>(revealedPlayers);

            if (names.Count == 0)
            {
                SetStatusSafe("No revealed players to open yet.", StatusKind.Info);
                return;
            }

            string region = string.IsNullOrEmpty(revealRegion) ? "na" : revealRegion;
            string url = MultiSearch.Build(liveSettings.Provider, region, names);

            try
            {
                Process.Start(url);
                SetStatusSafe("Opened multi-search for " + names.Count + " players.", StatusKind.Success);
            }
            catch (Exception ex)
            {
                SetStatusSafe("Couldn't open the browser: " + ex.Message, StatusKind.Error);
            }
        }

        // ---------- Dodge ----------

        // LCDS proxy call that quits champ select. It leaves the lobby/queue
        // (you take the normal dodge penalty) but keeps the client running.
        private async Task DodgeQuit()
        {
            string args = Uri.EscapeDataString("[\"\",\"teambuilder-draft\",\"quitV2\",\"\"]");
            await league.Post("/lol-login/v1/session/invoke?destination=lcdsServiceProxy&method=call&args=" + args, "{}");
        }

        private async void DodgeNow()
        {
            if (!EnsureConnected()) return;

            try
            {
                await DodgeQuit();
                SetStatusSafe("Dodged champ select.", StatusKind.Success);
            }
            catch (Exception ex)
            {
                SetStatusSafe("Dodge failed: " + ex.Message, StatusKind.Error);
            }
        }

        private void ResetDodge()
        {
            dodgeArmed = false;
            dodgeScheduled = false;
            UpdateDodgeUi();
        }

        private void OnChampSelectSession(JsonObject session)
        {
            if (!dodgeArmed || dodgeScheduled || session == null) return;
            if (!(session.TryGetValue("timer", out var tv) && tv is JsonObject timer)) return;

            timer.TryGetValue("phase", out var phaseValue);
            if ((phaseValue as string) != "FINALIZATION") return;

            timer.TryGetValue("adjustedTimeLeftInPhase", out var leftValue);
            long left = leftValue == null ? 0 : Convert.ToInt64(leftValue);

            dodgeScheduled = true;
            // Fire a touch before the phase ends so the quit registers in time.
            int wait = (int)Math.Max(0, left - 800);
            SetStatusSafe("Last-second dodge: quitting in ~" + Math.Round(wait / 1000.0) + "s.", StatusKind.Info);

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(wait);
                    if (!dodgeArmed) return;   // disarmed while waiting
                    await DodgeQuit();
                    SetStatusSafe("Dodged at the last second.", StatusKind.Success);
                }
                catch (Exception ex)
                {
                    SetStatusSafe("Last-second dodge failed: " + ex.Message, StatusKind.Error);
                }
                finally
                {
                    dodgeArmed = false;
                    dodgeScheduled = false;
                    UpdateDodgeUi();
                }
            });
        }

        private void UpdateDodgeUi()
        {
            var cb = dodgeArmCheckBox;
            if (cb == null || cb.IsDisposed) return;

            try
            {
                if (cb.InvokeRequired)
                {
                    cb.BeginInvoke((Action)UpdateDodgeUi);
                    return;
                }
            }
            catch
            {
                return;
            }

            // Reflect the armed state without re-triggering the user handler
            // (e.g. when the dodge fired or champ select ended).
            if (cb.Checked != dodgeArmed)
            {
                suppressDodgeEvent = true;
                cb.Checked = dodgeArmed;
                suppressDodgeEvent = false;
            }
        }

        // ---------- View ----------

        private void lobbyRevealButton_Click(object sender, EventArgs e)
        {
            ShowFeatureView(BuildLiveView, requireConnection: false);
        }

        private void BuildLiveView(Panel panel)
        {
            var layout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };

            layout.Controls.Add(MakeHeading("Live lobby"));
            layout.Controls.Add(MakeMutedLabel("Reveals your champ-select teammates from the local client and can auto-accept the ready check. Nothing leaves your PC.", 440));

            var autoAccept = new CheckBox { Text = "Auto-accept the ready check", Checked = liveSettings.AutoAccept, Margin = new Padding(0, 4, 0, 4) };
            Theme.StyleCheckBox(autoAccept);
            autoAccept.CheckedChanged += (s, e) => { liveSettings.AutoAccept = autoAccept.Checked; liveSettings.Save(); };

            var autoOpen = new CheckBox { Text = "Auto-open multi-search in champ select", Checked = liveSettings.AutoOpen, Margin = new Padding(0, 4, 0, 8) };
            Theme.StyleCheckBox(autoOpen);
            autoOpen.CheckedChanged += (s, e) => { liveSettings.AutoOpen = autoOpen.Checked; liveSettings.Save(); };

            var providerBox = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 0, 12) };
            Theme.StyleComboBox(providerBox);
            foreach (var provider in MultiSearch.Providers) providerBox.Items.Add(new Choice(provider.Label, provider.Value));
            providerBox.SelectedIndex = Math.Max(0, Array.FindIndex(MultiSearch.Providers, p => p.Value == liveSettings.Provider));
            providerBox.SelectedIndexChanged += (s, e) =>
            {
                if (providerBox.SelectedItem is Choice c) { liveSettings.Provider = c.Value; liveSettings.Save(); }
            };

            revealPhaseLabel = new Label
            {
                AutoSize = true,
                Font = Theme.SectionFont,
                ForeColor = Theme.TextMuted,
                Margin = new Padding(2, 0, 0, 6),
                Text = "Status: " + FriendlyPhase(currentPhase)
            };

            var openButton = new Button { Text = "Open multi-search", Size = new Size(200, 34), Margin = new Padding(0, 0, 0, 10) };
            Theme.StylePrimaryButton(openButton);
            openButton.Click += (s, e) =>
            {
                if (!EnsureConnected()) return;
                OpenMulti();
            };

            revealListPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0)
            };

            var dodgeNowButton = new Button { Text = "Dodge now", Size = new Size(200, 34), Margin = new Padding(0, 0, 0, 8) };
            Theme.StyleButton(dodgeNowButton);
            dodgeNowButton.Click += (s, e) => DodgeNow();

            dodgeArmCheckBox = new CheckBox { Text = "Dodge at last second", Checked = dodgeArmed, Margin = new Padding(0, 2, 0, 4) };
            Theme.StyleCheckBox(dodgeArmCheckBox);
            dodgeArmCheckBox.CheckedChanged += (s, e) =>
            {
                if (suppressDodgeEvent) return;
                dodgeArmed = dodgeArmCheckBox.Checked;
                dodgeScheduled = false;
                SetStatus(dodgeArmed
                    ? "Armed: Poro Tool will dodge just before the game starts. Uncheck to cancel."
                    : "Last-second dodge cancelled.", dodgeArmed ? StatusKind.Success : StatusKind.Info);
            };

            layout.Controls.Add(MakeLabeled("Lookup site", providerBox));
            layout.Controls.Add(autoAccept);
            layout.Controls.Add(autoOpen);
            layout.Controls.Add(revealPhaseLabel);
            layout.Controls.Add(openButton);
            layout.Controls.Add(MakeHeading("Revealed players"));
            layout.Controls.Add(revealListPanel);
            layout.Controls.Add(MakeHeading("Dodge"));
            layout.Controls.Add(MakeMutedLabel("Leaves champ select (normal dodge penalty) but keeps the client open. Tick last-second to dodge just before the game starts - untick any time to cancel.", 440));
            layout.Controls.Add(dodgeNowButton);
            layout.Controls.Add(dodgeArmCheckBox);
            panel.Controls.Add(layout);

            UpdateLiveUi();
        }

        private void UpdateLiveUi()
        {
            var panel = revealListPanel;
            if (panel == null || panel.IsDisposed) return;

            try
            {
                if (panel.InvokeRequired)
                {
                    panel.BeginInvoke((Action)UpdateLiveUi);
                    return;
                }
            }
            catch
            {
                return;   // control went away between the check and the invoke
            }

            if (revealPhaseLabel != null && !revealPhaseLabel.IsDisposed)
                revealPhaseLabel.Text = "Status: " + FriendlyPhase(currentPhase);

            List<string> names;
            lock (revealLock) names = new List<string>(revealedPlayers);

            panel.SuspendLayout();
            while (panel.Controls.Count > 0)
            {
                var c = panel.Controls[0];
                panel.Controls.RemoveAt(0);
                c.Dispose();
            }

            if (names.Count == 0)
            {
                panel.Controls.Add(new Label
                {
                    AutoSize = true,
                    Font = Theme.RowFont,
                    ForeColor = Theme.TextMuted,
                    Margin = new Padding(0, 2, 0, 2),
                    Text = currentPhase == "ChampSelect" ? "Loading players..." : "Waiting for champ select..."
                });
            }
            else
            {
                foreach (string name in names)
                {
                    panel.Controls.Add(new Label
                    {
                        AutoSize = true,
                        Font = Theme.RowFont,
                        ForeColor = Theme.TextPrimary,
                        Margin = new Padding(0, 2, 0, 2),
                        Text = name
                    });
                }
            }
            panel.ResumeLayout();
        }

        private static string FriendlyPhase(string phase)
        {
            switch (phase)
            {
                case "ChampSelect": return "In champ select";
                case "ReadyCheck": return "Ready check";
                case "Matchmaking": return "In queue";
                case "Lobby": return "In lobby";
                case "GameStart":
                case "InProgress": return "In game";
                case "":
                case "None": return "Idle";
                default: return phase;
            }
        }
    }
}
