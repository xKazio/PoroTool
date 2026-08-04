using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PoroTool
{
    /// <summary>
    /// Discord Rich Presence: puts what you're doing in League on your own
    /// Discord profile - queue, lobby state, your summoner icon and your rank.
    /// It connects to the Discord desktop app by itself and uses Riot's own
    /// "League of Legends" application id, so there is nothing to set up and
    /// the status looks exactly like the official one.
    /// </summary>
    public partial class MainForm
    {
        // Riot's own Discord application - this is what makes Discord label the
        // status "League of Legends" and show it on your profile.
        private const string LeagueAppId = "1402418696126992445";

        // Discord proxies plain https image urls, so the artwork comes straight
        // from Community Dragon instead of needing uploaded assets.
        // These crests are square and tightly cropped (the crest fills 40-75% of
        // the frame); the ranked-emblem art wastes 70-85% on transparent padding,
        // which left the rank almost invisible in Discord's small icon.
        private const string EmblemUrl = "https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-shared-components/global/default/";
        private const string ProfileIconUrl = "https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/v1/profile-icons/";

        private DiscordRpc discord;
        private System.Windows.Forms.Timer discordTimer;
        private long? discordStartedAt;
        private string discordLastKey = "";

        // Discord view controls; null while the view is not on screen.
        private Label discordStateLabel;

        private static readonly Dictionary<long, string> QueueNames = new Dictionary<long, string>
        {
            { 420, "Ranked Solo/Duo" }, { 440, "Ranked Flex" }, { 400, "Normal Draft" },
            { 430, "Normal Blind" }, { 450, "ARAM" }, { 490, "Quickplay" },
            { 700, "Clash" }, { 1700, "Arena" }, { 1090, "TFT Normal" },
            { 1100, "TFT Ranked" }, { 1160, "TFT Double Up" }, { 830, "Co-op vs AI" },
            { 840, "Co-op vs AI" }, { 850, "Co-op vs AI" }, { 0, "Custom" },
        };

        private void InitDiscord()
        {
            discord = new DiscordRpc();

            discordTimer = new System.Windows.Forms.Timer { Interval = 5000 };
            discordTimer.Tick += (s, e) => _ = UpdateDiscordPresence();

            // On by default and self-connecting: nothing for the user to do.
            if (liveSettings.DiscordEnabled) StartDiscord(announce: false);
        }

        private void StartDiscord(bool announce)
        {
            discordStartedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            discordLastKey = "";
            discordTimer.Start();

            if (!discord.Connect(LeagueAppId))
            {
                UpdateDiscordUi("Waiting for Discord...");
                if (announce) SetStatus("Discord isn't running yet - the status turns on by itself once it is.", StatusKind.Info);
                return;   // the timer keeps retrying
            }

            _ = UpdateDiscordPresence();
            if (announce) SetStatus("Discord status enabled.", StatusKind.Success);
        }

        private void StopDiscord(bool announce)
        {
            discordTimer?.Stop();
            try { discord?.ClearActivity(); } catch { }
            discord?.Disconnect();
            discordStartedAt = null;
            discordLastKey = "";
            UpdateDiscordUi("Off");
            if (announce) SetStatus("Discord status disabled.", StatusKind.Info);
        }

        private async Task UpdateDiscordPresence()
        {
            if (!liveSettings.DiscordEnabled) return;

            try
            {
                if (!discord.IsConnected && !discord.Connect(LeagueAppId))
                {
                    UpdateDiscordUi("Waiting for Discord...");
                    return;
                }

                string details, state, largeImage = "", largeText = "", smallImage = "", smallText = "";

                if (!league.IsConnected)
                {
                    details = "League of Legends";
                    state = "In Client";
                }
                else
                {
                    string phase = await league.Get("/lol-gameflow/v1/gameflow-phase") as string ?? "None";
                    details = await CurrentQueueNameAsync(phase);
                    state = await DiscordStateFor(phase);

                    var me = await league.Get("/lol-summoner/v1/current-summoner") as JsonObject;
                    if (me != null)
                    {
                        string name = StringOf(me, "gameName");
                        string tag = StringOf(me, "tagLine");
                        largeText = string.IsNullOrEmpty(tag) ? name : name + "#" + tag;

                        long iconId = LongOf(me, "profileIconId");
                        if (iconId > 0) largeImage = ProfileIconUrl + iconId + ".jpg";
                    }

                    if (liveSettings.DiscordShowRank)
                    {
                        var rank = await CurrentRankAsync();
                        smallText = rank.Text;
                        if (!string.IsNullOrEmpty(rank.Tier)) smallImage = EmblemUrl + rank.Tier + ".png";
                    }
                }

                // Restart the elapsed timer whenever the activity itself changes.
                string key = details + "|" + state + "|" + smallText;
                if (key != discordLastKey)
                {
                    discordLastKey = key;
                    discordStartedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                }

                discord.SetActivity(details, state, discordStartedAt,
                                    largeImage, largeText, smallImage, smallText);

                UpdateDiscordUi(details + " - " + state);
            }
            catch
            {
                // Client or Discord went away mid-update; the next tick retries.
            }
        }

        private async Task<string> DiscordStateFor(string phase)
        {
            switch (phase)
            {
                case "Lobby":
                    var lobby = await league.Get("/lol-lobby/v2/lobby") as JsonObject;
                    int members = 1, max = 0;
                    if (lobby != null)
                    {
                        if (lobby.TryGetValue("members", out var mv) && mv is JsonArray list) members = list.Count;
                        if (lobby.TryGetValue("gameConfig", out var gv) && gv is JsonObject config)
                            max = (int)LongOf(config, "maxLobbySize");
                    }
                    return max > 0 ? "In Lobby (" + members + "/" + max + ")" : "In Lobby";
                case "Matchmaking": return "In Queue";
                case "ReadyCheck": return "Match Found";
                case "ChampSelect": return "In Champion Select";
                case "GameStart":
                case "InProgress": return "In Game";
                case "WaitingForStats":
                case "PreEndOfGame":
                case "EndOfGame": return "Post-Game";
                default: return "In Client";
            }
        }

        private async Task<string> CurrentQueueNameAsync(string phase)
        {
            if (phase == "None" || phase == "") return "League of Legends";

            try
            {
                long queueId = -1;

                if (await league.Get("/lol-gameflow/v1/session") is JsonObject session &&
                    session.TryGetValue("gameData", out var gd) && gd is JsonObject gameData &&
                    gameData.TryGetValue("queue", out var qv) && qv is JsonObject queue)
                {
                    string name = StringOf(queue, "name");
                    if (!string.IsNullOrEmpty(name) && name != "Custom") return name;
                    queueId = LongOf(queue, "id");
                }

                if (queueId <= 0 && await league.Get("/lol-lobby/v2/lobby") is JsonObject lobby &&
                    lobby.TryGetValue("gameConfig", out var gv) && gv is JsonObject config)
                {
                    queueId = LongOf(config, "queueId");
                }

                if (QueueNames.TryGetValue(queueId, out var mapped)) return mapped;
            }
            catch { }

            return "League of Legends";
        }

        private async Task<(string Tier, string Text)> CurrentRankAsync()
        {
            try
            {
                if (!(await league.Get("/lol-ranked/v1/current-ranked-stats") is JsonObject stats)) return ("", "");
                if (!(stats.TryGetValue("queueMap", out var qm) && qm is JsonObject queueMap)) return ("", "");
                if (!(queueMap.TryGetValue(liveSettings.DiscordRankQueue, out var qv) && qv is JsonObject queue)) return ("", "");

                string tier = StringOf(queue, "tier");
                // Unranked has no emblem art, so it only shows as text.
                if (string.IsNullOrEmpty(tier) || tier == "NA" || tier == "NONE") return ("", "Unranked");

                string division = StringOf(queue, "division");
                long lp = LongOf(queue, "leaguePoints");

                string pretty = Capitalize(tier);
                // Apex tiers report division "NA" and don't show one in the client.
                if (!string.IsNullOrEmpty(division) && division != "NA") pretty += " " + division;

                return (tier.ToLowerInvariant(), pretty + ": " + lp + " LP");
            }
            catch
            {
                return ("", "");
            }
        }

        private static string Capitalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return char.ToUpperInvariant(value[0]) + value.Substring(1).ToLowerInvariant();
        }

        // ---------- View ----------

        private void discordPresenceButton_Click(object sender, EventArgs e)
        {
            ShowFeatureView(BuildDiscordView, requireConnection: false);
        }

        private void BuildDiscordView(Panel panel)
        {
            var layout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true
            };

            layout.Controls.Add(MakeHeading("Discord status"));
            layout.Controls.Add(MakeMutedLabel(
                "Shows League on your own Discord profile - queue, lobby state, your summoner icon " +
                "and your rank. It connects to Discord by itself; there is nothing to set up.", 460));

            var enabled = new CheckBox { Text = "Show my League status on Discord", Checked = liveSettings.DiscordEnabled, Margin = new Padding(0, 4, 0, 8) };
            Theme.StyleCheckBox(enabled);
            enabled.CheckedChanged += (s, e) =>
            {
                liveSettings.DiscordEnabled = enabled.Checked;
                liveSettings.Save();
                if (enabled.Checked) StartDiscord(announce: true);
                else StopDiscord(announce: true);
            };

            var showRank = new CheckBox { Text = "Show my rank on the small icon", Checked = liveSettings.DiscordShowRank, Margin = new Padding(0, 0, 0, 8) };
            Theme.StyleCheckBox(showRank);
            showRank.CheckedChanged += (s, e) => { liveSettings.DiscordShowRank = showRank.Checked; liveSettings.Save(); discordLastKey = ""; };

            var rankQueueBox = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 0, 10) };
            Theme.StyleComboBox(rankQueueBox);
            rankQueueBox.Items.Add(new Choice("Solo/Duo", "RANKED_SOLO_5x5"));
            rankQueueBox.Items.Add(new Choice("Flex", "RANKED_FLEX_SR"));
            rankQueueBox.Items.Add(new Choice("TFT", "RANKED_TFT"));
            for (int i = 0; i < rankQueueBox.Items.Count; i++)
                if (((Choice)rankQueueBox.Items[i]).Value == liveSettings.DiscordRankQueue) rankQueueBox.SelectedIndex = i;
            if (rankQueueBox.SelectedIndex < 0) rankQueueBox.SelectedIndex = 0;
            rankQueueBox.SelectedIndexChanged += (s, e) =>
            {
                if (rankQueueBox.SelectedItem is Choice c) { liveSettings.DiscordRankQueue = c.Value; liveSettings.Save(); discordLastKey = ""; }
            };

            discordStateLabel = new Label
            {
                AutoSize = true,
                Font = Theme.SectionFont,
                ForeColor = Theme.TextMuted,
                Margin = new Padding(2, 0, 0, 10),
                Text = "Status: " + (liveSettings.DiscordEnabled ? "starting..." : "Off")
            };

            layout.Controls.Add(enabled);
            layout.Controls.Add(showRank);
            layout.Controls.Add(MakeLabeled("Rank to show", rankQueueBox));
            layout.Controls.Add(discordStateLabel);
            layout.Controls.Add(MakeMutedLabel(
                "Tip: League can set its own Discord status too. Turn that off in the client " +
                "(Settings > Discord) so you don't see it twice.", 460));
            panel.Controls.Add(layout);
        }

        private void UpdateDiscordUi(string text)
        {
            if (InvokeRequired)
            {
                try { BeginInvoke((Action)(() => UpdateDiscordUi(text))); } catch { }
                return;
            }

            var label = discordStateLabel;
            if (label == null || label.IsDisposed) return;
            label.Text = "Status: " + text;
        }
    }
}
