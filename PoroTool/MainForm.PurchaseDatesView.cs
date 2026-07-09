using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace PoroTool
{
    /// <summary>
    /// Purchase dates view: every owned champion with the date it was bought,
    /// searchable and sortable by either column.
    /// </summary>
    public partial class MainForm
    {
        private sealed class PurchaseRow
        {
            public string Name;
            public long PurchasedMs;
        }

        private List<PurchaseRow> purchaseRows;

        private void purchaseDatesButton_Click(object sender, EventArgs e)
        {
            ShowFeatureView(BuildPurchaseDatesView);
        }

        private void BuildPurchaseDatesView(Panel panel)
        {
            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Clickable
            };
            Theme.StyleListView(listView);
            listView.Columns.Add("Champion", 260);
            listView.Columns.Add("Purchased", 200);

            var sorter = new PurchaseRowComparer();
            listView.ListViewItemSorter = sorter;
            listView.ColumnClick += (s, e) =>
            {
                if (sorter.Column == e.Column) sorter.Ascending = !sorter.Ascending;
                else { sorter.Column = e.Column; sorter.Ascending = true; }
                listView.Sort();
            };

            var topBar = new Panel { Dock = DockStyle.Top, Height = 36 };
            var searchTextBox = new TextBox { Width = 220, Location = new Point(0, 4) };
            Theme.StyleTextBox(searchTextBox);
            SetPlaceholder(searchTextBox, "Search champions...");
            searchTextBox.TextChanged += (s, e) => FillPurchaseList(listView, searchTextBox.Text);
            topBar.Controls.Add(searchTextBox);

            panel.Controls.Add(listView);
            panel.Controls.Add(topBar);

            LoadPurchaseDates(listView, viewCts.Token);
        }

        private async void LoadPurchaseDates(ListView listView, CancellationToken token)
        {
            SetStatus("Loading champions...");
            try
            {
                JsonObject summoner = (JsonObject)await league.Get("/lol-summoner/v1/current-summoner");
                if (summoner == null || !summoner.ContainsKey("summonerId"))
                {
                    SetStatus("Could not read the current summoner.", StatusKind.Error);
                    return;
                }
                long summonerId = Convert.ToInt64(summoner["summonerId"]);

                JsonArray champions = (JsonArray)await league.Get("/lol-champions/v1/inventories/" + summonerId + "/champions");
                if (champions == null)
                {
                    SetStatus("Could not read the champion inventory.", StatusKind.Error);
                    return;
                }

                var rows = new List<PurchaseRow>();
                foreach (JsonObject champion in champions)
                {
                    champion.TryGetValue("ownership", out var ownershipValue);
                    if (!(ownershipValue is JsonObject ownership)) continue;
                    ownership.TryGetValue("owned", out var owned);
                    if (!true.Equals(owned)) continue;

                    champion.TryGetValue("name", out var name);
                    champion.TryGetValue("purchased", out var purchasedMs);
                    rows.Add(new PurchaseRow
                    {
                        Name = name as string ?? "?",
                        PurchasedMs = purchasedMs == null ? 0 : Convert.ToInt64(purchasedMs)
                    });
                }

                if (token.IsCancellationRequested || listView.IsDisposed) return;

                purchaseRows = rows;
                FillPurchaseList(listView, "");
                SetStatus(rows.Count + " owned champions loaded.", StatusKind.Success);
            }
            catch (Exception ex)
            {
                SetStatus("Loading champions failed: " + ex.Message, StatusKind.Error);
            }
        }

        private void FillPurchaseList(ListView listView, string filter)
        {
            if (purchaseRows == null) return;

            filter = filter.Trim();
            listView.BeginUpdate();
            listView.Items.Clear();
            foreach (var row in purchaseRows)
            {
                if (filter.Length > 0 && row.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                // 0 means the champion was granted (reward/gift), not bought.
                string purchased = row.PurchasedMs == 0
                    ? "—"
                    : DateTimeOffset.FromUnixTimeMilliseconds(row.PurchasedMs).LocalDateTime.ToString("g");

                var item = new ListViewItem(new[] { row.Name, purchased }) { Tag = row.PurchasedMs };
                listView.Items.Add(item);
            }
            listView.Sort();
            listView.EndUpdate();
        }

        private sealed class PurchaseRowComparer : IComparer
        {
            public int Column = 1;
            public bool Ascending = true;

            public int Compare(object x, object y)
            {
                var a = (ListViewItem)x;
                var b = (ListViewItem)y;

                int result = Column == 0
                    ? string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase)
                    : ((long)a.Tag).CompareTo((long)b.Tag);
                return Ascending ? result : -result;
            }
        }

        private static void SetPlaceholder(TextBox textBox, string placeholder)
        {
            // net48 WinForms has no placeholder property; EM_SETCUEBANNER does it.
            NativeMethods.SendMessage(textBox.Handle, 0x1501, (IntPtr)1, placeholder);
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
        }
    }
}
