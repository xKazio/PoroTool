using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PoroTool
{
    /// <summary>
    /// Leaving champ select ("dodging"), shared by the Dodge now button, the
    /// last-second dodge and the ragequeue auto-dodge.
    ///
    /// Riot removed the old LCDS call (/lol-login/v1/session/invoke quitV2) -
    /// it is gone from the client and answers 404. What is left:
    ///  1. POST /lol-lobby-team-builder/champ-select/v1/session/quit - the call
    ///     the client's own quit button makes. The server only honours it where
    ///     the session has showQuitButton (customs etc.); ranked and normal
    ///     answer 400 "Current champ select does not allow quitting".
    ///  2. Closing the League client, which leaves champ select (a normal
    ///     dodge with the usual penalty). League is then started again through
    ///     the Riot Client, which keeps running, so it comes back on its own.
    /// </summary>
    public partial class MainForm
    {
        private enum DodgeOutcome { Left, Restarted, RestartedNoRelaunch, NotInChampSelect, Busy, Failed }

        private const string QuitChampSelectUrl = "/lol-lobby-team-builder/champ-select/v1/session/quit";
        private static readonly string[] LeagueProcessNames = { "LeagueClient", "LeagueClientUx", "LeagueClientUxRender" };

        private int dodgeBusy;

        /// <summary>True from closing League for a dodge until it is back,
        /// so the "client closed" status doesn't read like a crash.</summary>
        private volatile bool restartDodgeInProgress;

        /// <summary>Error text of the last failed quit request, for the status bar.</summary>
        private string lastDodgeError = "";

        /// <summary>
        /// Leaves the current champ select. Tries the quit endpoint first and,
        /// when <paramref name="allowRestart"/> is set and the server refuses,
        /// closes the League client and starts it again.
        /// <paramref name="gracefulMs"/> is how long League gets to close by
        /// itself before it is ended forcibly - short when the champ select
        /// timer is about to run out.
        /// </summary>
        private async Task<DodgeOutcome> DodgeAsync(bool allowRestart, int gracefulMs)
        {
            if (Interlocked.CompareExchange(ref dodgeBusy, 1, 0) != 0) return DodgeOutcome.Busy;
            try
            {
                lastDodgeError = "";
                if (!league.IsConnected) { lastDodgeError = "not connected"; return DodgeOutcome.Failed; }
                if (!await InChampSelectAsync()) return DodgeOutcome.NotInChampSelect;

                // 1) The client's own quit call.
                try
                {
                    var (status, content) = await league.Request("POST", QuitChampSelectUrl, null);
                    Log.Write("dodge: POST " + QuitChampSelectUrl + " -> HTTP " + status + " " + Log.Snippet(content));
                    if (status >= 200 && status < 300)
                    {
                        if (await WaitUntilOutOfChampSelectAsync(3000)) return DodgeOutcome.Left;
                    }
                    else
                    {
                        lastDodgeError = "HTTP " + status + ErrorDetail(content);
                    }
                }
                catch (Exception ex)
                {
                    lastDodgeError = ex.Message;
                    Log.Write("dodge: quit request failed: " + ex.Message);
                }

                if (!await InChampSelectAsync()) return DodgeOutcome.Left;
                if (!allowRestart) return DodgeOutcome.Failed;

                // 2) Close League and bring it back.
                return await RestartDodgeAsync(gracefulMs);
            }
            finally
            {
                Interlocked.Exchange(ref dodgeBusy, 0);
            }
        }

        private async Task<bool> InChampSelectAsync()
        {
            try
            {
                return league.IsConnected && await league.Get("/lol-gameflow/v1/gameflow-phase") is string phase && phase == "ChampSelect";
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> WaitUntilOutOfChampSelectAsync(int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (!await InChampSelectAsync()) return true;
                await Task.Delay(150);
            }
            return false;
        }

        private async Task<DodgeOutcome> RestartDodgeAsync(int gracefulMs)
        {
            // Everything needed to start League again has to be read now:
            // once it closes, its API and command line are gone.
            string riotPort = league.LastRiotClientPort, riotToken = league.LastRiotClientToken;
            string patchline = "live";
            try
            {
                if (await league.Get("/system/v1/builds") is JsonObject builds && StringOf(builds, "patchline").Length > 0)
                    patchline = StringOf(builds, "patchline");
            }
            catch { }

            restartDodgeInProgress = true;
            SetStatusSafe("Dodging: closing the League client (it starts again by itself)...", StatusKind.Info);
            Log.Write("dodge: quit refused, restarting the client (graceful " + gracefulMs + " ms, patchline " + patchline + ")");

            try
            {
                var (status, _) = await league.Request("POST", "/process-control/v1/process/quit", null);
                Log.Write("dodge: process quit -> HTTP " + status);
            }
            catch (Exception ex)
            {
                // The connection can drop mid-request as the client goes down.
                Log.Write("dodge: process quit request ended: " + ex.Message);
            }

            if (!await WaitForLeagueExitAsync(gracefulMs))
            {
                Log.Write("dodge: client still running after " + gracefulMs + " ms - ending it");
                KillLeagueProcesses();
                if (!await WaitForLeagueExitAsync(10000))
                {
                    restartDodgeInProgress = false;
                    lastDodgeError = "the League client could not be closed (try running Poro Tool as administrator)";
                    Log.Write("dodge: client did not exit");
                    return DodgeOutcome.Failed;
                }
            }

            // Give the Riot Client a moment to notice League is gone.
            await Task.Delay(1500);

            bool relaunched = false;
            for (int attempt = 0; attempt < 3 && !relaunched; attempt++)
            {
                if (attempt > 0) await Task.Delay(2000);
                relaunched = await RiotClientApi.LaunchLeagueAsync(riotPort, riotToken, patchline);
            }

            if (!relaunched)
            {
                restartDodgeInProgress = false;
                return DodgeOutcome.RestartedNoRelaunch;
            }

            // Clear the flag if the client never reconnects, so a later
            // unrelated close isn't reported as a dodge restart.
            _ = Task.Delay(TimeSpan.FromMinutes(3)).ContinueWith(_ => restartDodgeInProgress = false);
            return DodgeOutcome.Restarted;
        }

        private static bool LeagueRunning()
        {
            foreach (string name in LeagueProcessNames)
            {
                var processes = Process.GetProcessesByName(name);
                bool any = processes.Length > 0;
                foreach (var process in processes) process.Dispose();
                if (any) return true;
            }
            return false;
        }

        private static async Task<bool> WaitForLeagueExitAsync(int timeoutMs)
        {
            var watch = Stopwatch.StartNew();
            while (LeagueRunning())
            {
                if (watch.ElapsedMilliseconds >= timeoutMs) return false;
                await Task.Delay(100);
            }
            return true;
        }

        private static void KillLeagueProcesses()
        {
            // The backend first: it is the one connected to champ select.
            foreach (string name in LeagueProcessNames)
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try { process.Kill(); }
                    catch (Exception ex) { Log.Write("dodge: could not end " + name + ": " + ex.Message); }
                    finally { process.Dispose(); }
                }
            }
        }

        /// <summary>Status-bar text for a dodge outcome.</summary>
        private string DodgeOutcomeText(DodgeOutcome outcome, string what)
        {
            switch (outcome)
            {
                case DodgeOutcome.Left: return what + ": left champ select.";
                case DodgeOutcome.Restarted: return what + ": dodged by restarting League - it is starting again.";
                case DodgeOutcome.RestartedNoRelaunch: return what + ": dodged, but League couldn't be started again - start it from the Riot Client.";
                case DodgeOutcome.NotInChampSelect: return what + ": not in champ select.";
                case DodgeOutcome.Busy: return what + ": a dodge is already running.";
                default: return what + " failed" + (lastDodgeError.Length > 0 ? " (" + lastDodgeError + ")" : "") + ".";
            }
        }

        private static StatusKind DodgeOutcomeKind(DodgeOutcome outcome)
        {
            switch (outcome)
            {
                case DodgeOutcome.Left:
                case DodgeOutcome.Restarted: return StatusKind.Success;
                case DodgeOutcome.Failed:
                case DodgeOutcome.RestartedNoRelaunch: return StatusKind.Error;
                default: return StatusKind.Info;
            }
        }

        private static bool Dodged(DodgeOutcome outcome)
        {
            return outcome == DodgeOutcome.Left || outcome == DodgeOutcome.Restarted || outcome == DodgeOutcome.RestartedNoRelaunch;
        }

        // ---------- Dodge now ----------

        private async void DodgeNow()
        {
            if (!EnsureConnected()) return;

            SetStatusSafe("Dodging champ select...", StatusKind.Info);
            try
            {
                var outcome = await DodgeAsync(allowRestart: true, gracefulMs: await GracefulCloseMsAsync());
                SetStatusSafe(DodgeOutcomeText(outcome, "Dodge"), DodgeOutcomeKind(outcome));
            }
            catch (Exception ex)
            {
                SetStatusSafe("Dodge failed: " + ex.Message, StatusKind.Error);
            }
        }

        /// <summary>
        /// How long League may take to close by itself during a restart-dodge:
        /// a few seconds normally, much less when the finalization timer is
        /// about to run out (the client must be gone before the game starts).
        /// </summary>
        private async Task<int> GracefulCloseMsAsync()
        {
            var timer = await ChampSelectTimerAsync();
            if (timer.Phase != "FINALIZATION") return 4000;
            return (int)Math.Max(300, Math.Min(4000, timer.MsLeft - 3000));
        }

        /// <summary>
        /// The champ-select timer. adjustedTimeLeftInPhase is a snapshot taken
        /// at internalNowInEpochMs (local clock) and only refreshes when the
        /// session changes, so the time left is computed from both.
        /// </summary>
        private async Task<(string Phase, long MsLeft, bool QuitAllowed)> ChampSelectTimerAsync()
        {
            try
            {
                if (await league.Get("/lol-champ-select/v1/session") is JsonObject session)
                    return TimerOf(session);
            }
            catch { }
            return ("", 0, false);
        }

        private static (string Phase, long MsLeft, bool QuitAllowed) TimerOf(JsonObject session)
        {
            bool quitAllowed = BoolOf(session, "showQuitButton");
            if (!(session.TryGetValue("timer", out var tv) && tv is JsonObject timer)) return ("", 0, quitAllowed);

            string phase = StringOf(timer, "phase");
            long left = LongOf(timer, "adjustedTimeLeftInPhase");
            long snapshot = LongOf(timer, "internalNowInEpochMs");
            if (snapshot > 0)
                left = snapshot + left - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return (phase, left, quitAllowed);
        }

        // ---------- Last-second dodge ----------

        // How long before the end of finalization the dodge fires. The quit
        // call is instant; closing the client needs a few seconds of margin so
        // the game can never start with the client gone (that would be a leave,
        // not a dodge).
        private const int LastSecondQuitMs = 1200;
        private const int LastSecondRestartMs = 5000;

        // Armed state, touched from the UI thread, the websocket thread and the
        // watcher task - always under dodgeLock.
        private readonly object dodgeLock = new object();
        private volatile bool dodgeArmed;
        private bool dodgeSawChampSelect;
        private CancellationTokenSource lastSecondCts;

        /// <summary>
        /// Arms or disarms the last-second dodge. Arming outside champ select
        /// is fine: it waits for the next champ select and covers that one only.
        /// </summary>
        private void SetLastSecondDodge(bool armed)
        {
            lock (dodgeLock)
            {
                dodgeArmed = armed;
                dodgeSawChampSelect = false;
                lastSecondCts?.Cancel();
                lastSecondCts = null;

                if (armed && currentPhase == "ChampSelect") StartLastSecondWatch();
            }
            UpdateDodgeUi();
        }

        /// <summary>Gameflow hook: starts watching in champ select, disarms once that champ select is over.</summary>
        private void OnDodgePhase(string phase)
        {
            bool disarm = false;
            lock (dodgeLock)
            {
                if (!dodgeArmed) return;

                if (phase == "ChampSelect") StartLastSecondWatch();
                else if (dodgeSawChampSelect) disarm = true;   // that champ select ended one way or another
            }
            if (disarm) SetLastSecondDodge(false);
        }

        // Caller holds dodgeLock.
        private void StartLastSecondWatch()
        {
            if (lastSecondCts != null) return;
            dodgeSawChampSelect = true;
            var cts = new CancellationTokenSource();
            lastSecondCts = cts;
            Task.Run(() => LastSecondWatchAsync(cts));
        }

        /// <summary>
        /// Polls the champ-select timer (instead of relying on session events,
        /// which stop arriving when nothing changes) and fires the dodge just
        /// before finalization runs out.
        /// </summary>
        private async Task LastSecondWatchAsync(CancellationTokenSource cts)
        {
            var token = cts.Token;
            bool announced = false;
            try
            {
                while (!token.IsCancellationRequested && dodgeArmed)
                {
                    JsonObject session = null;
                    try { session = await league.Get("/lol-champ-select/v1/session") as JsonObject; }
                    catch { }

                    if (session == null || !session.ContainsKey("timer"))
                    {
                        if (!await InChampSelectAsync()) return;
                        await Task.Delay(500, token);
                        continue;
                    }

                    var timer = TimerOf(session);
                    int fireAt = timer.QuitAllowed ? LastSecondQuitMs : LastSecondRestartMs;

                    if (timer.Phase == "FINALIZATION")
                    {
                        if (!announced)
                        {
                            announced = true;
                            SetStatusSafe("Last-second dodge armed: fires ~" + Math.Round(fireAt / 1000.0) + "s before the game starts" +
                                          (timer.QuitAllowed ? "." : " (closes and restarts League)."), StatusKind.Info);
                        }

                        if (timer.MsLeft <= fireAt)
                        {
                            Log.Write("last-second dodge: firing with " + timer.MsLeft + " ms left (quit allowed: " + timer.QuitAllowed + ")");
                            var outcome = await DodgeAsync(allowRestart: true,
                                gracefulMs: (int)Math.Max(300, Math.Min(1500, timer.MsLeft - 2500)));
                            SetStatusSafe(DodgeOutcomeText(outcome, "Last-second dodge"), DodgeOutcomeKind(outcome));
                            return;
                        }

                        // Re-read often enough to land within a few hundred ms.
                        await Task.Delay((int)Math.Max(50, Math.Min(400, timer.MsLeft - fireAt)), token);
                    }
                    else if (timer.Phase == "GAME_STARTING")
                    {
                        SetStatusSafe("Last-second dodge: too late, the game is already starting.", StatusKind.Error);
                        return;
                    }
                    else
                    {
                        await Task.Delay(500, token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SetStatusSafe("Last-second dodge failed: " + ex.Message, StatusKind.Error);
            }
            finally
            {
                bool finishedOnItsOwn = false;
                lock (dodgeLock)
                {
                    if (ReferenceEquals(lastSecondCts, cts))
                    {
                        // Fired or gave up (champ select over): disarm.
                        lastSecondCts = null;
                        dodgeArmed = false;
                        dodgeSawChampSelect = false;
                        finishedOnItsOwn = true;
                    }
                }
                cts.Dispose();
                if (finishedOnItsOwn) UpdateDodgeUi();
            }
        }

        private void UpdateDodgeUi()
        {
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)UpdateDodgeUi); } catch { }
                return;
            }

            var cb = dodgeArmCheckBox;
            if (cb == null || cb.IsDisposed) return;

            // Reflect the armed state without re-triggering the user handler
            // (e.g. when the dodge fired or champ select ended).
            if (cb.Checked != dodgeArmed)
            {
                suppressDodgeEvent = true;
                cb.Checked = dodgeArmed;
                suppressDodgeEvent = false;
            }
        }
    }
}
