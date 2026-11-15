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
    /// Buy missing champions: lists every champion you don't own with its Blue
    /// Essence price and buys them cheapest first, one by one, until the
    /// essence runs out - champions you can't afford are simply left alone.
    /// Only Blue Essence is ever offered as payment, never RP.
    ///
    /// A purchase goes the way the client's own unlock button sends it
    /// (POST /lol-purchase-widget/v2/purchaseItems, currency IP = Blue Essence).
    /// Champions sold in the new shop (lol-shoppefront) fall back to that
    /// shop's Blue Essence option. Every purchase is confirmed by reading the
    /// champion back as owned before the next one starts.
    /// </summary>
    public partial class MainForm
    {
        private const string ChampionItemTypeId = "7fdbb3e4-c36f-4b3c-9088-b794ff127ff3";

        private sealed class ChampionOffer
        {
            public long Id;
            public string Name;
            public string InstanceId;
            public long Price;
            // New-shop listing with a Blue Essence payment option, when there is one.
            public string StoreId;
            public string CatalogEntryId;
            public string PaymentOptionKey;
        }

        private bool buyingChampions;

        private void buyChampionsButton_Click(object sender, EventArgs e)
        {
            ShowFeatureView(BuildBuyChampionsView);
        }

        private void BuildBuyChampionsView(Panel panel)
        {
            var token = viewCts.Token;

            var listView = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            Theme.StyleListView(listView);
            listView.Columns.Add("Champion", 220);
            listView.Columns.Add("Blue Essence", 110);
            listView.Columns.Add("", 220);

            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(0, 0, 0, 8)
            };
            top.Controls.Add(MakeHeading("Buy missing champions"));
            top.Controls.Add(MakeMutedLabel("Buys every champion you don't own with Blue Essence, cheapest first, until the essence runs out. " +
                                            "The ones you can't afford are left alone. Never spends RP.", 460));

            var summary = new Label
            {
                AutoSize = true,
                Font = Theme.SectionFont,
                ForeColor = Theme.TextMuted,
                Margin = new Padding(2, 4, 0, 8),
                Text = "Loading..."
            };
            top.Controls.Add(summary);

            var buyButton = new Button { Text = "Buy", Size = new Size(260, 36), Margin = new Padding(0, 0, 0, 4), Enabled = false };
            Theme.StylePrimaryButton(buyButton);
            top.Controls.Add(buyButton);

            panel.Controls.Add(listView);
            panel.Controls.Add(top);

            List<ChampionOffer> missing = null;
            long essence = 0;

            async Task Reload()
            {
                buyButton.Enabled = false;
                summary.Text = "Loading...";
                try
                {
                    (missing, essence) = await LoadMissingChampionsAsync();
                    if (token.IsCancellationRequested || listView.IsDisposed) return;

                    var plan = PlanPurchases(missing, essence);
                    FillBuyList(listView, missing, plan);

                    long cost = plan.Sum(c => c.Price);
                    summary.Text = missing.Count == 0
                        ? "You own every champion. Blue Essence: " + essence.ToString("N0")
                        : missing.Count + " missing - Blue Essence: " + essence.ToString("N0") +
                          " - affordable now: " + plan.Count + " (" + cost.ToString("N0") + " BE)";
                    buyButton.Text = plan.Count == 0 ? "Nothing affordable to buy"
                                   : "Buy " + plan.Count + " champion" + (plan.Count == 1 ? "" : "s") + " for " + cost.ToString("N0") + " BE";
                    buyButton.Enabled = plan.Count > 0 && !buyingChampions;
                    SetStatus("Champion shop loaded.", StatusKind.Success);
                }
                catch (Exception ex)
                {
                    if (summary.IsDisposed) return;
                    summary.Text = "Couldn't load the champion shop.";
                    SetStatus("Loading the champion shop failed: " + ex.Message, StatusKind.Error);
                }
            }

            buyButton.Click += async (s, e) =>
            {
                if (missing == null || buyingChampions) return;
                var plan = PlanPurchases(missing, essence);
                if (plan.Count == 0) return;

                long cost = plan.Sum(c => c.Price);
                var skipped = missing.Count - plan.Count;
                string names = string.Join(", ", plan.Take(25).Select(c => c.Name)) + (plan.Count > 25 ? ", ..." : "");
                var answer = MessageBox.Show(this,
                    "Buy " + plan.Count + " champion" + (plan.Count == 1 ? "" : "s") + " for " + cost.ToString("N0") +
                    " Blue Essence (you have " + essence.ToString("N0") + ")?\n\n" + names +
                    (skipped > 0 ? "\n\n" + skipped + " more stay unbought - not enough Blue Essence." : "") +
                    "\n\nOnly Blue Essence is used, never RP.",
                    "Buy champions", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) return;

                buyingChampions = true;
                buyButton.Enabled = false;
                try
                {
                    await BuyChampionsAsync(plan, listView);
                }
                finally
                {
                    buyingChampions = false;
                }
                if (!token.IsCancellationRequested && !listView.IsDisposed) await Reload();
            };

            _ = Reload();
        }

        /// <summary>Cheapest first, as many as the essence covers; the rest is skipped.</summary>
        private static List<ChampionOffer> PlanPurchases(List<ChampionOffer> missing, long essence)
        {
            var plan = new List<ChampionOffer>();
            long left = essence;
            foreach (var champion in missing.OrderBy(c => c.Price).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (champion.Price > left) continue;   // a cheaper one later may still fit
                plan.Add(champion);
                left -= champion.Price;
            }
            return plan;
        }

        private void FillBuyList(ListView listView, List<ChampionOffer> missing, List<ChampionOffer> plan)
        {
            var planned = new HashSet<long>(plan.Select(c => c.Id));
            listView.BeginUpdate();
            listView.Items.Clear();
            foreach (var champion in missing.OrderBy(c => c.Price).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                var item = new ListViewItem(new[]
                {
                    champion.Name,
                    champion.Price.ToString("N0"),
                    planned.Contains(champion.Id) ? "will be bought" : "not enough Blue Essence"
                })
                { Tag = champion.Id };
                if (!planned.Contains(champion.Id)) item.ForeColor = Theme.TextMuted;
                listView.Items.Add(item);
            }
            listView.EndUpdate();
        }

        private static void SetBuyRowStatus(ListView listView, long championId, string text, Color color)
        {
            if (listView.IsDisposed) return;
            foreach (ListViewItem item in listView.Items)
            {
                if (!(item.Tag is long id) || id != championId) continue;
                item.SubItems[2].Text = text;
                item.ForeColor = color;
                item.EnsureVisible();
                return;
            }
        }

        /// <summary>Unowned, buyable champions with their Blue Essence price, plus the current essence.</summary>
        private async Task<(List<ChampionOffer> Missing, long Essence)> LoadMissingChampionsAsync()
        {
            if (!(await league.Get("/lol-catalog/v1/items/CHAMPION") is JsonArray catalog))
                throw new InvalidOperationException("the champion catalog is not available yet");

            // The owned list is a second opinion: never offer something already owned.
            var owned = new HashSet<long>();
            if (await league.Get("/lol-champions/v1/owned-champions-minimal") is JsonArray ownedList)
            {
                foreach (JsonObject champion in ownedList)
                {
                    if (champion.TryGetValue("ownership", out var ov) && ov is JsonObject ownership && BoolOf(ownership, "owned"))
                        owned.Add(LongOf(champion, "id"));
                }
            }

            var missing = new List<ChampionOffer>();
            foreach (JsonObject item in catalog)
            {
                long id = LongOf(item, "itemId");
                if (id <= 0 || BoolOf(item, "owned") || owned.Contains(id)) continue;
                if (item.TryGetValue("active", out var active) && false.Equals(active)) continue;

                long price = 0;
                if (item.TryGetValue("prices", out var pv) && pv is JsonArray prices)
                    foreach (JsonObject p in prices)
                        if (StringOf(p, "currency") == "IP") price = LongOf(p, "cost");
                if (price <= 0) continue;   // not sold for Blue Essence

                missing.Add(new ChampionOffer
                {
                    Id = id,
                    Name = StringOf(item, "name"),
                    InstanceId = StringOf(item, "itemInstanceId"),
                    Price = price
                });
            }

            await AttachShopListingsAsync(missing);

            long essence = 0;
            if (await league.Get("/lol-inventory/v1/wallet/lol_blue_essence") is JsonObject wallet)
                essence = LongOf(wallet, "lol_blue_essence");

            return (missing, essence);
        }

        /// <summary>Finds each champion's listing in the new shop and its Blue Essence payment option.</summary>
        private async Task AttachShopListingsAsync(List<ChampionOffer> missing)
        {
            try
            {
                if (!(await league.Get("/lol-shoppefront/v1/stores") is JsonObject response) ||
                    !(response.TryGetValue("data", out var dv) && dv is JsonArray stores))
                    return;

                var byInstance = missing.Where(c => c.InstanceId.Length > 0).ToDictionary(c => c.InstanceId, StringComparer.OrdinalIgnoreCase);
                foreach (JsonObject store in stores)
                {
                    if (!(store.TryGetValue("catalogEntries", out var ev) && ev is JsonArray entries)) continue;
                    foreach (JsonObject entry in entries)
                    {
                        if (!(entry.TryGetValue("purchaseUnits", out var uv) && uv is JsonArray units) || units.Count != 1) continue;
                        if (!(units[0] is JsonObject unit) || !(unit.TryGetValue("fulfillment", out var fv) && fv is JsonObject fulfillment)) continue;
                        if (StringOf(fulfillment, "itemTypeId") != ChampionItemTypeId) continue;
                        if (!byInstance.TryGetValue(StringOf(fulfillment, "itemId"), out var champion) || champion.StoreId != null) continue;
                        if (!(unit.TryGetValue("paymentOptions", out var ov) && ov is JsonArray options)) continue;

                        foreach (JsonObject option in options)
                        {
                            // Exactly one payment, and it must be Blue Essence.
                            if (!(option.TryGetValue("payments", out var pv) && pv is JsonArray payments) || payments.Count != 1) continue;
                            if (!(payments[0] is JsonObject payment) || StringOf(payment, "name") != "lol_blue_essence") continue;

                            champion.StoreId = StringOf(store, "id");
                            champion.CatalogEntryId = StringOf(entry, "id");
                            champion.PaymentOptionKey = StringOf(option, "key");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // The new shop is only a fallback; the regular purchase still works without it.
                Log.Write("buy champions: reading the shop failed: " + ex.Message);
            }
        }

        private async Task BuyChampionsAsync(List<ChampionOffer> plan, ListView listView)
        {
            int bought = 0, failed = 0;
            string lastError = "";

            for (int i = 0; i < plan.Count; i++)
            {
                var champion = plan[i];
                if (!league.IsConnected) { lastError = "the League client closed"; break; }

                // Re-check the real balance before every purchase.
                long essence = 0;
                if (await league.Get("/lol-inventory/v1/wallet/lol_blue_essence") is JsonObject wallet)
                    essence = LongOf(wallet, "lol_blue_essence");
                if (essence < champion.Price)
                {
                    SetBuyRowStatus(listView, champion.Id, "not enough Blue Essence", Theme.TextMuted);
                    continue;
                }

                SetStatus("Buying " + champion.Name + " (" + (i + 1) + "/" + plan.Count + ")...");
                SetBuyRowStatus(listView, champion.Id, "buying...", Theme.TextPrimary);

                var (ok, error) = await BuyChampionAsync(champion);
                if (ok)
                {
                    bought++;
                    SetBuyRowStatus(listView, champion.Id, "bought", Theme.Success);
                }
                else
                {
                    failed++;
                    lastError = error;
                    SetBuyRowStatus(listView, champion.Id, "failed" + (error.Length > 0 ? ": " + error : ""), Theme.Error);
                }

                await Task.Delay(400);   // don't hammer the store
            }

            if (failed == 0)
                SetStatus("Bought " + bought + " champion" + (bought == 1 ? "" : "s") + ".", StatusKind.Success);
            else
                SetStatus("Bought " + bought + ", " + failed + " failed" + (lastError.Length > 0 ? " (" + lastError + ")" : "") +
                          " - details in poro.log.", bought > 0 ? StatusKind.Info : StatusKind.Error);
        }

        /// <summary>Buys one champion with Blue Essence and confirms it is owned afterwards.</summary>
        private async Task<(bool Ok, string Error)> BuyChampionAsync(ChampionOffer champion)
        {
            string error = "";

            // 1) The client's own unlock purchase.
            try
            {
                string body = "{\"items\": [{\"itemKey\": {\"inventoryType\": \"CHAMPION\", \"itemId\": " + champion.Id +
                              ", \"subInventoryType\": \"\"}, \"purchaseCurrencyInfo\": {\"currencyType\": \"IP\", \"price\": " + champion.Price +
                              ", \"purchasable\": true}, \"source\": \"cdp\", \"quantity\": 1}]}";
                var (status, content) = await league.Request("POST", "/lol-purchase-widget/v2/purchaseItems", body);
                Log.Write("buy " + champion.Name + " (" + champion.Id + ", " + champion.Price + " BE) via purchase widget -> HTTP " + status + " " + Log.Snippet(content, 300));
                if (status >= 200 && status < 300)
                {
                    if (await WaitUntilOwnedAsync(champion.Id, 8000)) return (true, "");
                    error = "the purchase went through but the champion didn't show up as owned";
                }
                else
                {
                    error = "HTTP " + status + ErrorDetail(content);
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Write("buy " + champion.Name + " via purchase widget failed: " + ex.Message);
            }

            if (await IsOwnedAsync(champion.Id)) return (true, "");
            if (champion.StoreId == null) return (false, error);

            // 2) The new shop, with its Blue Essence option.
            try
            {
                string body = "{\"purchaseItems\": [{\"storeId\": \"" + champion.StoreId + "\", \"catalogEntryId\": \"" + champion.CatalogEntryId +
                              "\", \"quantity\": 1, \"paymentOptions\": [" + SimpleJson.SerializeObject(champion.PaymentOptionKey) + "]}], \"purchaseTimeOut\": 15000}";
                var (status, content) = await league.Request("POST", "/lol-shoppefront/v1/bulk-purchases", body);
                Log.Write("buy " + champion.Name + " via shop (" + champion.PaymentOptionKey + ") -> HTTP " + status + " " + Log.Snippet(content, 300));
                if (status < 200 || status >= 300) return (false, "HTTP " + status + ErrorDetail(content));

                string purchaseId = (SimpleJson.DeserializeObject(content) as string ?? "").Trim();
                if (purchaseId.Length > 0)
                {
                    // The shop answers with an id; the result shows up on it.
                    for (int attempt = 0; attempt < 40; attempt++)
                    {
                        await Task.Delay(500);
                        if (!(await league.Get("/lol-shoppefront/v1/purchases/" + Uri.EscapeDataString(purchaseId)) is JsonObject result)) continue;
                        string state = StringOf(result, "status");
                        if (state == "SUCCESS") break;
                        if (state == "FAILURE")
                        {
                            Log.Write("buy " + champion.Name + " via shop: " + Log.Snippet(SimpleJson.SerializeObject(result), 300));
                            return (false, "the shop refused the purchase");
                        }
                    }
                }

                if (await WaitUntilOwnedAsync(champion.Id, 8000)) return (true, "");
                return (false, "the champion didn't show up as owned");
            }
            catch (Exception ex)
            {
                Log.Write("buy " + champion.Name + " via shop failed: " + ex.Message);
                return (false, ex.Message);
            }
        }

        private async Task<bool> IsOwnedAsync(long championId)
        {
            try
            {
                return await league.Get("/lol-purchase-widget/v1/purchasable-item?inventoryType=CHAMPION&itemId=" + championId) is JsonObject info &&
                       info.TryGetValue("item", out var iv) && iv is JsonObject item && BoolOf(item, "owned");
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> WaitUntilOwnedAsync(long championId, int timeoutMs)
        {
            for (int waited = 0; waited < timeoutMs; waited += 500)
            {
                if (await IsOwnedAsync(championId)) return true;
                await Task.Delay(500);
            }
            return await IsOwnedAsync(championId);
        }
    }
}
