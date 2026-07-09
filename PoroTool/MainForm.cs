using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PoroTool
{
    public partial class MainForm : Form
    {
        private const string DisenchantLabel = "Disenchant";
        private const string UpgradeLabel = "Upgrade";
        private const string ForgeEmoteLabel = "Forge into permanent";
        private const string ForgeEggLabel = "Forge into egg";

        private static readonly Dictionary<string, string> RecipeTypeByLabel = new Dictionary<string, string>
        {
            { DisenchantLabel, "DISENCHANT" },
            { UpgradeLabel, "UPGRADE" },
            { ForgeEmoteLabel, "FORGE" },
            { ForgeEggLabel, "FORGE" },
        };

        private enum LootCategory { None, Champion, Skin, Emote, Wardskin, Icon, Companion, Eternals, Chest }
        private enum StatusKind { Info, Success, Error }

        private LeagueConnection league;

        private readonly Dictionary<string, List<JsonObject>> lootByCategory = new Dictionary<string, List<JsonObject>>();
        private readonly Dictionary<string, List<string>> queuedCrafts = new Dictionary<string, List<string>>();

        private LootCategory currentCategory = LootCategory.None;
        private string currentCategoryKey;
        private string selectedRecipeLabel;

        private JsonObject lootTranslations;
        private List<Tuple<string, JsonObject>> chestRecipes = new List<Tuple<string, JsonObject>>();

        public MainForm()
        {
            InitializeComponent();
            Icon = new Icon(typeof(MainForm), "logo.ico");
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            BackColor = Theme.Canvas;
            sidebar.BackColor = Theme.Sidebar;
            outputPanel.BackColor = Theme.Canvas;
            featurePanel.BackColor = Theme.Canvas;
            statusStrip.BackColor = Theme.Sidebar;
            statusDivider.BackColor = Theme.Hairline;

            messageLabel.Font = Theme.StatusFont;
            messageLabel.ForeColor = Theme.TextMuted;

            lootSectionLabel.Font = Theme.SectionFont;
            lootSectionLabel.ForeColor = Theme.TextMuted;
            profileSectionLabel.Font = Theme.SectionFont;
            profileSectionLabel.ForeColor = Theme.TextMuted;
            processSectionLabel.Font = Theme.SectionFont;
            processSectionLabel.ForeColor = Theme.TextMuted;

            foreach (var button in new[] { loadChampionsButton, loadSkinsButton, loadEmotesButton, loadWardsButton,
                                           loadIconsButton, loadCompanionsButton, loadEternalsButton, loadChestsButton,
                                           chatRankButton, backgroundButton, statusButton, purchaseDatesButton,
                                           customApiButton, removeTokensButton })
            {
                Theme.StyleButton(button);
            }
            Theme.StylePrimaryButton(processButton);
            Theme.StyleTextButton(legalNoteButton);
            processOptions.BackColor = Theme.Sidebar;

            socialsPanel.BackColor = Theme.Sidebar;
            Theme.StyleSocialButton(discordButton, Theme.DiscordBrand, Theme.DiscordBrandHover, LoadEmbeddedIcon("discord.png"));
            Theme.StyleSocialButton(githubButton, Theme.GitHubBrand, Theme.GitHubBrandHover, LoadEmbeddedIcon("github.png"));
        }

        private static Image LoadEmbeddedIcon(string name)
        {
            using (var full = new Bitmap(typeof(MainForm), name))
                return new Bitmap(full, new Size(18, 18));
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            league = new LeagueConnection();
            league.OnConnected += () => SetStatusSafe("Connected to the League client.", StatusKind.Success);
            league.OnDisconnected += () => SetStatusSafe("League client closed. Waiting for it to come back...", StatusKind.Error);
            SetStatus("Waiting for the League client...");
            FillProcessOptions();
        }

        private async void loadChampionsButton_Click(object sender, EventArgs e)
        {
            await ShowCategory("CHAMPION", LootCategory.Champion);
        }

        private async void loadSkinsButton_Click(object sender, EventArgs e)
        {
            await ShowCategory("SKIN", LootCategory.Skin);
        }

        private async void loadEmotesButton_Click(object sender, EventArgs e)
        {
            await ShowCategory("EMOTE", LootCategory.Emote);
        }

        private async void loadWardsButton_Click(object sender, EventArgs e)
        {
            await ShowCategory("WARDSKIN", LootCategory.Wardskin);
        }

        private async void loadIconsButton_Click(object sender, EventArgs e)
        {
            await ShowCategory("SUMMONERICON", LootCategory.Icon);
        }

        private async void loadCompanionsButton_Click(object sender, EventArgs e)
        {
            await ShowCategory("COMPANION", LootCategory.Companion);
        }

        private async void loadEternalsButton_Click(object sender, EventArgs e)
        {
            await ShowCategory("ETERNALS", LootCategory.Eternals);
        }

        private async void loadChestsButton_Click(object sender, EventArgs e)
        {
            await ShowCategory("CHEST", LootCategory.Chest);
        }

        private async void removeTokensButton_Click(object sender, EventArgs e)
        {
            if (!EnsureConnected()) return;

            await league.Post("/lol-challenges/v1/update-player-preferences/", "{\"challengeIds\": []}");
            SetStatus("Challenge tokens removed from your profile banner.", StatusKind.Success);
        }

        private void SetStatus(string message, StatusKind kind = StatusKind.Info)
        {
            messageLabel.Text = message;
            messageLabel.ForeColor = kind == StatusKind.Success ? Theme.Success
                                   : kind == StatusKind.Error ? Theme.Error
                                   : Theme.TextMuted;
        }

        private void SetStatusSafe(string message, StatusKind kind = StatusKind.Info)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => SetStatus(message, kind)));
                return;
            }
            SetStatus(message, kind);
        }

        private bool EnsureConnected()
        {
            if (league.IsConnected) return true;

            SetStatus("Not connected to the League client. Log in first and give it a few seconds; running Poro Tool as admin can help too.", StatusKind.Error);
            return false;
        }

        private async Task ShowCategory(string categoryKey, LootCategory category)
        {
            if (!EnsureConnected()) return;

            ShowLootView();
            await RefreshLoot();
            outputPanel.Controls.Clear();

            if (!lootByCategory.ContainsKey(categoryKey))
            {
                SetStatus("No loot found in this category.", StatusKind.Error);
                currentCategory = LootCategory.None;
                currentCategoryKey = null;
                return;
            }

            if (category == LootCategory.Chest)
                RenderChests(lootByCategory[categoryKey]);
            else
                RenderShards(lootByCategory[categoryKey]);

            currentCategory = category;
            currentCategoryKey = categoryKey;
            FillProcessOptions();
            SetStatus("Loaded.", StatusKind.Success);
        }

        private async Task RefreshLoot()
        {
            lootByCategory.Clear();

            JsonObject playerLoot = (JsonObject)await league.Get("/lol-loot/v1/player-loot-map");
            if (playerLoot == null) return;
            playerLoot.Remove("");

            for (int i = 0; i < playerLoot.Count; i++)
            {
                JsonObject item = (JsonObject)playerLoot[i];
                string category = (string)item["displayCategories"];
                if (category.Equals("")) category = "Unknown";

                if (!lootByCategory.TryGetValue(category, out var items))
                {
                    items = new List<JsonObject>();
                    lootByCategory.Add(category, items);
                }
                items.Add(item);
            }
        }

        private void FillProcessOptions()
        {
            processOptions.Controls.Clear();
            selectedRecipeLabel = null;

            string[] labels;
            switch (currentCategory)
            {
                case LootCategory.Companion:
                    labels = new[] { ForgeEggLabel };
                    break;
                case LootCategory.Emote:
                    labels = new[] { DisenchantLabel, ForgeEmoteLabel };
                    break;
                default:
                    labels = new[] { DisenchantLabel, UpgradeLabel };
                    break;
            }

            foreach (string label in labels)
            {
                var option = new Button
                {
                    Text = label,
                    Size = new Size(196, 30),
                    Margin = new Padding(0, 0, 0, 4)
                };
                Theme.StyleButton(option);
                option.Click += (s, e) => SelectProcessOption((Button)s);
                processOptions.Controls.Add(option);
            }
        }

        private void SelectProcessOption(Button option)
        {
            selectedRecipeLabel = option.Text;
            foreach (Button other in processOptions.Controls)
            {
                bool selected = other == option;
                other.FlatAppearance.BorderColor = selected ? Theme.Pink : Theme.Hairline;
                other.BackColor = selected ? Theme.SurfaceHover : Theme.Surface;
            }
        }

        private void RenderShards(List<JsonObject> items)
        {
            long totalShards = items.Sum(item => (long)item["count"]);

            AddShardRow(items.Count, items.Count, "Unique shards", null, header: true);
            AddShardRow(totalShards, totalShards, "All shards", null, header: true, extraBottomMargin: true);

            foreach (JsonObject item in items)
            {
                long count = (long)item["count"];
                string name = (string)item["localizedName"];
                if (name.Equals("")) name = (string)item["itemDesc"];

                string itemType = (string)item["type"];
                if (itemType == "SKIN_RENTAL") name += " (Shard)";
                else if (itemType == "SKIN") name += " (Permanent)";

                AddShardRow(0, count, count + "x " + name, (string)item["lootId"]);
            }
        }

        private void AddShardRow(decimal minimum, decimal maximum, string text, string controlName, bool header = false, bool extraBottomMargin = false)
        {
            var amount = new NumericUpDown
            {
                Minimum = minimum,
                Maximum = maximum,
                Value = maximum,
                Width = 56,
                Margin = new Padding(0, 3, 8, 3)
            };
            if (controlName != null) amount.Name = controlName;
            Theme.StyleNumeric(amount);

            var label = new Label
            {
                Text = text,
                AutoSize = false,
                Width = 330,
                Height = 25,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = header ? Theme.RowHeaderFont : Theme.RowFont,
                ForeColor = Theme.TextPrimary,
                Margin = new Padding(0, 3, 24, extraBottomMargin ? 12 : 3)
            };

            outputPanel.Controls.Add(amount);
            outputPanel.Controls.Add(label);
        }

        private void RenderChests(List<JsonObject> items)
        {
            ComboBox chestsComboBox = new ComboBox
            {
                Name = nameof(chestsComboBox),
                Width = 240,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 3, 8, 3)
            };
            Theme.StyleComboBox(chestsComboBox);
            chestsComboBox.SelectedValueChanged += OnChestMaterialChanged;
            chestsComboBox.Items.Add("Select material (" + items.Count + " found)");
            outputPanel.Controls.Add(chestsComboBox);
            chestsComboBox.SelectedIndex = 0;

            foreach (JsonObject item in items)
            {
                long count = (long)item["count"];
                string lootId = (string)item["lootId"];
                string lootName = (string)item["lootName"];
                string name = (string)item["localizedName"];

                if (name.Equals(""))
                {
                    if (lootTranslations == null)
                        LoadTranslationsFromCommunityDragon();

                    if (lootName.StartsWith("CHAMPION_TOKEN_"))
                        name = (string)lootTranslations["loot_name_" + lootName.ToLower()] + (string)item["itemDesc"];
                    else if (lootName.EndsWith("MATERIAL_key_fragment"))
                        name = (string)lootTranslations["loot_name_" + lootName.ToLower() + "[other]"] + (string)item["itemDesc"];
                    else
                        name = (string)lootTranslations["loot_name_" + lootId.ToLower()];
                }

                chestsComboBox.Items.Add(new Material(count, name));
            }

            NumericUpDown chestsRepeatNumericUpDown = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 999999,
                Name = nameof(chestsRepeatNumericUpDown),
                Width = 56,
                Margin = new Padding(0, 3, 8, 3)
            };
            Theme.StyleNumeric(chestsRepeatNumericUpDown);

            chestsComboBox.SelectedIndexChanged += (s, e) =>
            {
                if (chestsComboBox.SelectedItem is not Material material) return;

                chestsRepeatNumericUpDown.Maximum = material.Count;
                chestsRepeatNumericUpDown.Value = material.Count;
            };

            var repeatLabel = new Label
            {
                Text = "Repeat:",
                AutoSize = false,
                Width = 60,
                Height = 25,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = Theme.RowFont,
                ForeColor = Theme.TextMuted,
                Margin = new Padding(0, 3, 4, 3)
            };

            ComboBox chestsRecipeComboBox = new ComboBox
            {
                Name = nameof(chestsRecipeComboBox),
                Width = 240,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 3, 8, 3)
            };
            Theme.StyleComboBox(chestsRecipeComboBox);
            chestsRecipeComboBox.Items.Add("Select recipe (0 found)");

            outputPanel.Controls.Add(repeatLabel);
            outputPanel.Controls.Add(chestsRepeatNumericUpDown);
            outputPanel.Controls.Add(chestsRecipeComboBox);
            chestsRecipeComboBox.SelectedIndex = 0;
        }

        private sealed class Material
        {
            public long Count { get; }
            public string Name { get; }

            public Material(long count, string name)
            {
                Count = count;
                Name = name;
            }

            public override string ToString()
            {
                return $"{Count}x {Name}";
            }
        }

        private async void processButton_Click(object sender, EventArgs e)
        {
            switch (currentCategory)
            {
                case LootCategory.None:
                    MessageBox.Show("Load a loot category before processing.");
                    break;
                case LootCategory.Chest:
                    await CraftFromChest();
                    break;
                default:
                    await CraftLoot(lootByCategory[currentCategoryKey]);
                    break;
            }
        }

        private async Task CraftLoot(List<JsonObject> items)
        {
            if (selectedRecipeLabel == null)
            {
                MessageBox.Show("Select a processing type first.");
                return;
            }
            string recipeType = RecipeTypeByLabel[selectedRecipeLabel];

            foreach (JsonObject item in items)
            {
                string lootId = (string)item["lootId"];
                var amount = (NumericUpDown)outputPanel.Controls[lootId];
                decimal repeat = amount.Value;
                if (repeat == 0) continue;

                var recipes = (JsonArray)await league.Get("/lol-loot/v1/recipes/initial-item/" + lootId);
                foreach (JsonObject recipe in recipes)
                {
                    if (!recipe["type"].Equals(recipeType)) continue;

                    string url = "/lol-loot/v1/recipes/" + (string)recipe["recipeName"] + "/craft?repeat=" + repeat;
                    QueueCraft(url, "[\"" + lootId + "\"]");
                    break;
                }
            }

            await SendQueuedCrafts();
            MessageBox.Show("Processed!");
        }

        private async Task CraftFromChest()
        {
            var chestsComboBox = (ComboBox)outputPanel.Controls["chestsComboBox"];
            if (chestsComboBox.SelectedIndex <= 0)
            {
                MessageBox.Show("Select a material before processing.");
                return;
            }
            var chestsRecipeComboBox = (ComboBox)outputPanel.Controls["chestsRecipeComboBox"];
            if (chestsRecipeComboBox.SelectedIndex <= 0)
            {
                MessageBox.Show("Select a recipe before processing.");
                return;
            }
            var chestsRepeatNumericUpDown = (NumericUpDown)outputPanel.Controls["chestsRepeatNumericUpDown"];
            if (chestsRepeatNumericUpDown.Value <= 0)
            {
                MessageBox.Show("Repeat at least once.");
                return;
            }

            JsonObject recipe = chestRecipes[chestsRecipeComboBox.SelectedIndex - 1].Item2;
            var lootIds = new List<string>();
            foreach (JsonObject slot in (JsonArray)recipe["slots"])
            {
                var slotLootIds = (JsonArray)slot["lootIds"];
                if (slotLootIds.Count != 0)
                    lootIds.AddRange(slotLootIds.Cast<string>());
                else
                    lootIds.Add("MATERIAL_key");
            }
            string body = "[" + string.Join(",", lootIds.Select(id => "\"" + id + "\"")) + "]";

            string url = "/lol-loot/v1/recipes/" + recipe["recipeName"] + "/craft?repeat=" + chestsRepeatNumericUpDown.Value;
            await league.Post(url, body);

            MessageBox.Show("Processed!");
        }

        private async void OnChestMaterialChanged(object sender, EventArgs e)
        {
            var chestsComboBox = (ComboBox)outputPanel.Controls["chestsComboBox"];
            if (chestsComboBox.SelectedIndex <= 0) return;

            var chestsRecipeComboBox = (ComboBox)outputPanel.Controls["chestsRecipeComboBox"];
            chestsRecipeComboBox.Items.Clear();
            chestsRecipeComboBox.ResetText();

            JsonObject material = lootByCategory["CHEST"][chestsComboBox.SelectedIndex - 1];
            var recipes = (JsonArray)await league.Get("/lol-loot/v1/recipes/initial-item/" + material["lootId"]);

            chestsRecipeComboBox.Items.Add("Select recipe (" + recipes.Count + " found)");

            chestRecipes = new List<Tuple<string, JsonObject>>();
            foreach (JsonObject recipe in recipes)
            {
                string recipeName = (string)recipe["contextMenuText"];
                if (recipeName.Equals("")) recipeName = (string)recipe["description"];
                chestRecipes.Add(Tuple.Create(recipeName, recipe));
            }
            chestRecipes = chestRecipes.OrderBy(r => r.Item1).ToList();

            foreach (var recipe in chestRecipes)
                chestsRecipeComboBox.Items.Add(recipe.Item1);
            chestsRecipeComboBox.SelectedIndex = 0;
        }

        private void LoadTranslationsFromCommunityDragon()
        {
            // Some loot entries come back from the LCU without a localized name;
            // CommunityDragon hosts the client's translation table.
            using (var wc = new WebClient())
            {
                var json = wc.DownloadString("https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-loot/global/en_us/trans.json");
                lootTranslations = (JsonObject)SimpleJson.DeserializeObject(json);
            }
        }

        private void QueueCraft(string url, string body)
        {
            if (!queuedCrafts.TryGetValue(url, out var bodies))
            {
                bodies = new List<string>();
                queuedCrafts.Add(url, bodies);
            }
            bodies.Add(body);
        }

        private async Task SendQueuedCrafts()
        {
            foreach (var craft in queuedCrafts)
            {
                foreach (string body in craft.Value)
                    await league.Post(craft.Key, body);
            }
            queuedCrafts.Clear();
        }

        private void discordButton_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("https://discord.com/users/599000053956476937");
        }

        private void githubButton_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/xKazio/PoroTool");
        }

        private void legalNoteButton_Click(object sender, EventArgs e)
        {
            MessageBox.Show("Poro Tool isn't endorsed by Riot Games and doesn't reflect the views or opinions of Riot Games " +
                            "or anyone officially involved in producing or managing League of Legends. League of Legends and " +
                            "Riot Games are trademarks or registered trademarks of Riot Games, Inc. League of Legends © Riot Games, Inc.");
        }
    }
}
