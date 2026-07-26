using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PoroTool
{
    /// <summary>
    /// Friends cleanup view: the full friends list with checkboxes, a search
    /// filter, and bulk unfriend/block actions. A "never played with" filter
    /// was tried and removed: the client only keeps the recent ~100 games of
    /// match history, so it could never be accurate.
    /// </summary>
    public partial class MainForm
    {
        private sealed class FriendRow
        {
            public string Id;          // jid used by /lol-chat (puuid@region.pvp.net)
            public string Puuid;
            public string GameName;
            public string GameTag;
            public string Display;
            public string Note;
            public string Availability;
            public long LastSeenMs;
            public long SummonerId;
            public long Icon;
            public bool Selected;
        }

        private void friendsCleanupButton_Click(object sender, EventArgs e)
        {
            ShowFeatureView(BuildFriendsView);
        }

        private void BuildFriendsView(Panel panel)
        {
            var token = viewCts.Token;
            var rows = new List<FriendRow>();
            bool suppressChecks = false;
            bool busy = false;

            // ----- controls -----

            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Clickable
            };
            Theme.StyleListView(listView);
            listView.Columns.Add("Friend", 260);
            listView.Columns.Add("Status", 150);
            listView.Columns.Add("Note", 170);

            var sorter = new FriendRowComparer();
            listView.ListViewItemSorter = sorter;
            listView.ColumnClick += (s, e) =>
            {
                if (sorter.Column == e.Column) sorter.Ascending = !sorter.Ascending;
                else { sorter.Column = e.Column; sorter.Ascending = true; }
                listView.Sort();
            };

            var topBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true
            };

            var searchTextBox = new TextBox { Width = 220, Margin = new Padding(0, 0, 8, 0) };
            Theme.StyleTextBox(searchTextBox);

            var refreshButton = new Button { Text = "Refresh", Size = new Size(90, 27), Margin = new Padding(0, 0, 8, 0) };
            Theme.StyleButton(refreshButton);
            refreshButton.TextAlign = ContentAlignment.MiddleCenter;
            refreshButton.Padding = Padding.Empty;

            var selectAllButton = new Button { Text = "Select all", Size = new Size(90, 27), Margin = new Padding(0, 0, 8, 0) };
            Theme.StyleButton(selectAllButton);
            selectAllButton.TextAlign = ContentAlignment.MiddleCenter;
            selectAllButton.Padding = Padding.Empty;

            var selectNoneButton = new Button { Text = "Select none", Size = new Size(90, 27), Margin = new Padding(0) };
            Theme.StyleButton(selectNoneButton);
            selectNoneButton.TextAlign = ContentAlignment.MiddleCenter;
            selectNoneButton.Padding = Padding.Empty;

            var searchRow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 8)
            };
            searchRow.Controls.Add(searchTextBox);
            searchRow.Controls.Add(refreshButton);
            searchRow.Controls.Add(selectAllButton);
            searchRow.Controls.Add(selectNoneButton);

            topBar.Controls.Add(MakeHeading("Friends cleanup"));
            topBar.Controls.Add(MakeMutedLabel("Tick the friends to remove, then unfriend or block them in one go. Selection survives searching; Select all only takes the search results.", 460));
            topBar.Controls.Add(searchRow);

            var countLabel = new Label
            {
                AutoSize = true,
                Font = Theme.RowFont,
                ForeColor = Theme.TextMuted,
                Margin = new Padding(0, 8, 16, 0),
                Text = "Selected 0 of 0"
            };

            var unfriendButton = new Button { Text = "Unfriend selected", Size = new Size(170, 34), Margin = new Padding(0, 0, 8, 0), Enabled = false };
            Theme.StylePrimaryButton(unfriendButton);

            var blockButton = new Button { Text = "Block selected", Size = new Size(170, 34), Margin = new Padding(0), Enabled = false };
            Theme.StylePrimaryButton(blockButton);
            blockButton.ForeColor = Theme.Error;
            blockButton.FlatAppearance.BorderColor = Theme.Error;

            var bottomBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                WrapContents = false,
                AutoSize = true,
                Padding = new Padding(0, 8, 0, 0)
            };
            bottomBar.Controls.Add(countLabel);
            bottomBar.Controls.Add(unfriendButton);
            bottomBar.Controls.Add(blockButton);

            panel.Controls.Add(listView);
            panel.Controls.Add(bottomBar);
            panel.Controls.Add(topBar);
            SetPlaceholder(searchTextBox, "Search friends...");

            // ----- helpers -----

            void UpdateCounts()
            {
                int selected = rows.Count(r => r.Selected);
                countLabel.Text = "Selected " + selected + " of " + rows.Count;
                unfriendButton.Enabled = blockButton.Enabled = selected > 0 && !busy;
            }

            void SetBusy(bool value)
            {
                busy = value;
                refreshButton.Enabled = selectAllButton.Enabled = selectNoneButton.Enabled =
                    listView.Enabled = !value;
                UpdateCounts();
            }

            void FillList()
            {
                string filter = searchTextBox.Text.Trim();
                suppressChecks = true;
                listView.BeginUpdate();
                listView.Items.Clear();
                foreach (var row in rows)
                {
                    if (filter.Length > 0 &&
                        row.Display.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                        row.Note.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    var item = new ListViewItem(new[] { row.Display, FriendlyAvailability(row.Availability, row.LastSeenMs), row.Note })
                    {
                        Tag = row,
                        Checked = row.Selected
                    };
                    listView.Items.Add(item);
                }
                listView.Sort();
                listView.EndUpdate();
                suppressChecks = false;
                UpdateCounts();
            }

            async Task LoadFriendsAsync()
            {
                SetStatus("Loading friends...");
                try
                {
                    if (!(await league.Get("/lol-chat/v1/friends") is JsonArray friends))
                    {
                        SetStatus("Could not read the friends list.", StatusKind.Error);
                        return;
                    }

                    var loaded = new List<FriendRow>();
                    foreach (JsonObject friend in friends)
                    {
                        string id = StringOf(friend, "id");
                        if (id.Length == 0) continue;

                        string name = FirstString(friend, "gameName", "name");
                        if (name.Length == 0) name = id.Split('@')[0];
                        string tag = StringOf(friend, "gameTag");

                        loaded.Add(new FriendRow
                        {
                            Id = id,
                            Puuid = StringOf(friend, "puuid"),
                            GameName = StringOf(friend, "gameName"),
                            GameTag = tag,
                            Display = tag.Length > 0 ? name + "#" + tag : name,
                            Note = StringOf(friend, "note"),
                            Availability = StringOf(friend, "availability"),
                            LastSeenMs = LongOf(friend, "lastSeenOnlineTimestamp"),
                            SummonerId = LongOf(friend, "summonerId"),
                            Icon = LongOf(friend, "icon")
                        });
                    }
                    loaded.Sort((a, b) => string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase));

                    if (token.IsCancellationRequested || listView.IsDisposed) return;

                    rows = loaded;
                    FillList();
                    SetStatus(rows.Count + " friends loaded.", StatusKind.Success);
                }
                catch (Exception ex)
                {
                    SetStatus("Loading friends failed: " + ex.Message, StatusKind.Error);
                }
            }

            async Task RemoveSelectedAsync(bool block)
            {
                if (!EnsureConnected()) return;

                var targets = rows.Where(r => r.Selected).ToList();
                if (targets.Count == 0) return;

                string names = string.Join("\n", targets.Take(12).Select(t => "•  " + t.Display));
                if (targets.Count > 12) names += "\n…and " + (targets.Count - 12) + " more";
                string title = block ? "Block friends" : "Unfriend";
                string question = (block
                    ? "Block " + targets.Count + " friend(s)? They are also removed from your friends list."
                    : "Remove " + targets.Count + " friend(s) from your friends list?")
                    + "\n\n" + names + "\n\nThis cannot be undone from Poro Tool.";
                if (MessageBox.Show(this, question, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;

                SetBusy(true);
                int done = 0, failed = 0;
                try
                {
                    foreach (var target in targets)
                    {
                        if (token.IsCancellationRequested) break;

                        SetStatus((block ? "Blocking " : "Removing ") + (done + failed + 1) + "/" + targets.Count + ": " + target.Display);
                        try
                        {
                            bool success;
                            if (block)
                            {
                                var body = new JsonObject
                                {
                                    ["id"] = target.Id,
                                    ["pid"] = target.Id,
                                    ["puuid"] = target.Puuid,
                                    ["summonerId"] = target.SummonerId,
                                    ["gameName"] = target.GameName,
                                    ["gameTag"] = target.GameTag,
                                    ["name"] = "",
                                    ["icon"] = target.Icon
                                };
                                var (status, _) = await league.Request("POST", "/lol-chat/v1/blocked-players", SimpleJson.SerializeObject(body));
                                success = status >= 200 && status < 300;

                                // Blocking normally unfriends too; make sure it did.
                                if (success)
                                    await league.Request("DELETE", "/lol-chat/v1/friends/" + Uri.EscapeDataString(target.Id), null);
                            }
                            else
                            {
                                var (status, _) = await league.Request("DELETE", "/lol-chat/v1/friends/" + Uri.EscapeDataString(target.Id), null);
                                success = (status >= 200 && status < 300) || status == 404;
                            }

                            if (success) done++; else failed++;
                        }
                        catch
                        {
                            failed++;
                        }

                        await Task.Delay(50);   // don't hammer the client
                    }
                }
                finally
                {
                    if (!listView.IsDisposed) SetBusy(false);
                }

                string verb = block ? "Blocked " : "Removed ";
                SetStatus(failed == 0
                    ? verb + done + " friend(s)."
                    : verb + done + " friend(s), " + failed + " failed.", failed == 0 ? StatusKind.Success : StatusKind.Error);

                if (!token.IsCancellationRequested && !listView.IsDisposed)
                    await LoadFriendsAsync();
            }

            // ----- events -----

            listView.ItemChecked += (s, e) =>
            {
                if (suppressChecks || !(e.Item.Tag is FriendRow row)) return;
                row.Selected = e.Item.Checked;
                UpdateCounts();
            };

            searchTextBox.TextChanged += (s, e) => FillList();

            refreshButton.Click += async (s, e) =>
            {
                if (!EnsureConnected()) return;
                await LoadFriendsAsync();
            };

            selectAllButton.Click += (s, e) =>
            {
                // Applies to the visible (filtered) items only, on purpose.
                foreach (ListViewItem item in listView.Items)
                    ((FriendRow)item.Tag).Selected = true;
                FillList();
            };

            selectNoneButton.Click += (s, e) =>
            {
                foreach (var row in rows) row.Selected = false;
                FillList();
            };

            unfriendButton.Click += async (s, e) => await RemoveSelectedAsync(block: false);
            blockButton.Click += async (s, e) => await RemoveSelectedAsync(block: true);

            if (EnsureConnected()) _ = LoadFriendsAsync();
        }

        private static string StringOf(JsonObject obj, string key)
        {
            return obj != null && obj.TryGetValue(key, out var value) ? value as string ?? "" : "";
        }

        private static long LongOf(JsonObject obj, string key)
        {
            if (obj == null || !obj.TryGetValue(key, out var value) || value == null) return 0;
            try { return Convert.ToInt64(value); } catch { return 0; }
        }

        private static string FriendlyAvailability(string availability, long lastSeenMs)
        {
            switch (availability)
            {
                case "chat": return "Online";
                case "away": return "Away";
                case "dnd": return "Busy / in game";
                case "mobile": return "Mobile";
                default:
                    return lastSeenMs == 0
                        ? "Offline"
                        : "Seen " + DateTimeOffset.FromUnixTimeMilliseconds(lastSeenMs).LocalDateTime.ToString("g");
            }
        }

        private sealed class FriendRowComparer : IComparer
        {
            public int Column;
            public bool Ascending = true;

            public int Compare(object x, object y)
            {
                var a = (FriendRow)((ListViewItem)x).Tag;
                var b = (FriendRow)((ListViewItem)y).Tag;

                int result;
                switch (Column)
                {
                    case 1: result = OfflineRank(a).CompareTo(OfflineRank(b)); break;
                    case 2: result = string.Compare(a.Note, b.Note, StringComparison.OrdinalIgnoreCase); break;
                    default: result = 0; break;
                }
                if (result == 0)
                    result = string.Compare(a.Display, b.Display, StringComparison.OrdinalIgnoreCase);
                return Ascending ? result : -result;
            }

            // Online states first, then the most recently seen.
            private static long OfflineRank(FriendRow row)
            {
                return row.Availability != "offline" && row.Availability.Length > 0 ? long.MinValue : -row.LastSeenMs;
            }
        }
    }
}
