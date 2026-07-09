using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace PoroTool
{
    /// <summary>
    /// Profile features ported from league-profile-tool: view switching
    /// between the loot panel and the feature panel, plus the chat rank,
    /// chat status and API console views. The profile background and
    /// purchase date views live in their own files.
    /// </summary>
    public partial class MainForm
    {
        private CancellationTokenSource viewCts;

        private void ShowLootView()
        {
            viewCts?.Cancel();
            revealListPanel = null;
            revealPhaseLabel = null;
            dodgeArmCheckBox = null;
            featurePanel.Visible = false;
            DisposeFeatureControls();
            ImageCache.ClearSplashCache();
            outputPanel.Visible = true;
        }

        private void ShowFeatureView(Action<Panel> build, bool requireConnection = true)
        {
            if (requireConnection && !EnsureConnected()) return;

            revealListPanel = null;
            revealPhaseLabel = null;
            dodgeArmCheckBox = null;

            viewCts?.Cancel();
            viewCts = new CancellationTokenSource();

            outputPanel.Visible = false;
            featurePanel.SuspendLayout();
            DisposeFeatureControls();
            ImageCache.ClearSplashCache();
            build(featurePanel);
            featurePanel.ResumeLayout();
            featurePanel.Visible = true;

            // The loot process button only acts on a loaded loot category.
            currentCategory = LootCategory.None;
            currentCategoryKey = null;
            FillProcessOptions();
        }

        private void DisposeFeatureControls()
        {
            // Controls.Clear() alone leaks window handles. The controls are
            // disposed, but never the images they show: those belong to ImageCache.
            while (featurePanel.Controls.Count > 0)
            {
                var control = featurePanel.Controls[0];
                featurePanel.Controls.RemoveAt(0);
                control.Dispose();
            }
        }

        private Label MakeHeading(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Font = Theme.RowHeaderFont,
                ForeColor = Theme.TextPrimary,
                Margin = new Padding(0, 0, 0, 4)
            };
        }

        private Label MakeMutedLabel(string text, int width)
        {
            return new Label
            {
                Text = text,
                AutoSize = false,
                Width = width,
                Height = 34,
                Font = Theme.StatusFont,
                ForeColor = Theme.TextMuted,
                Margin = new Padding(0, 0, 0, 8)
            };
        }

        private sealed class Choice
        {
            public string Text { get; }
            public string Value { get; }

            public Choice(string text, string value)
            {
                Text = text;
                Value = value;
            }

            public override string ToString()
            {
                return Text;
            }
        }

        // ---------- Chat rank ----------

        private static readonly string[] ChatRankTiers =
            { "IRON", "BRONZE", "SILVER", "GOLD", "PLATINUM", "EMERALD", "DIAMOND", "MASTER", "GRANDMASTER", "CHALLENGER" };
        private static readonly string[] ChatRankDivisions = { "I", "II", "III", "IV", "NA" };
        private static readonly string[] ApexTiers = { "MASTER", "GRANDMASTER", "CHALLENGER" };

        private void chatRankButton_Click(object sender, EventArgs e)
        {
            ShowFeatureView(BuildChatRankView);
        }

        private void BuildChatRankView(Panel panel)
        {
            var layout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };

            layout.Controls.Add(MakeHeading("Chat rank"));
            layout.Controls.Add(MakeMutedLabel("The rank friends see when hovering your icon in chat. Visual only.", 420));

            var queueBox = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 0, 8) };
            Theme.StyleComboBox(queueBox);
            queueBox.Items.Add(new Choice("Solo/Duo", "RANKED_SOLO_5x5"));
            queueBox.Items.Add(new Choice("TFT", "RANKED_TFT"));
            queueBox.SelectedIndex = 0;

            var tierBox = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 0, 8) };
            Theme.StyleComboBox(tierBox);
            tierBox.Items.AddRange(ChatRankTiers);
            tierBox.SelectedIndex = Array.IndexOf(ChatRankTiers, "CHALLENGER");

            var divisionBox = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 0, 12) };
            Theme.StyleComboBox(divisionBox);
            divisionBox.Items.AddRange(ChatRankDivisions);
            divisionBox.SelectedIndex = Array.IndexOf(ChatRankDivisions, "NA");

            // Apex tiers have no division in the client.
            tierBox.SelectedIndexChanged += (s, e) =>
            {
                if (Array.IndexOf(ApexTiers, (string)tierBox.SelectedItem) >= 0)
                    divisionBox.SelectedIndex = Array.IndexOf(ChatRankDivisions, "NA");
            };

            var setButton = new Button { Text = "Set rank", Size = new Size(220, 34) };
            Theme.StylePrimaryButton(setButton);
            setButton.Click += async (s, e) =>
            {
                if (!EnsureConnected()) return;

                var body = new JsonObject
                {
                    ["lol"] = new JsonObject
                    {
                        ["rankedLeagueQueue"] = ((Choice)queueBox.SelectedItem).Value,
                        ["rankedLeagueTier"] = (string)tierBox.SelectedItem,
                        ["rankedLeagueDivision"] = (string)divisionBox.SelectedItem,
                    }
                };

                try
                {
                    var (status, _) = await league.Put("/lol-chat/v1/me/", SimpleJson.SerializeObject(body));
                    if (status >= 200 && status < 300)
                        SetStatus("Chat rank updated.", StatusKind.Success);
                    else
                        SetStatus("Setting the chat rank failed (HTTP " + status + ").", StatusKind.Error);
                }
                catch (Exception ex)
                {
                    SetStatus("Setting the chat rank failed: " + ex.Message, StatusKind.Error);
                }
            };

            layout.Controls.Add(MakeLabeled("Queue", queueBox));
            layout.Controls.Add(MakeLabeled("Tier", tierBox));
            layout.Controls.Add(MakeLabeled("Division", divisionBox));
            layout.Controls.Add(setButton);
            panel.Controls.Add(layout);
        }

        private Control MakeLabeled(string text, Control control)
        {
            var row = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0)
            };
            row.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                Font = Theme.SectionFont,
                ForeColor = Theme.TextMuted,
                Margin = new Padding(2, 0, 0, 2)
            });
            row.Controls.Add(control);
            return row;
        }

        // ---------- Chat status ----------

        private void statusButton_Click(object sender, EventArgs e)
        {
            ShowFeatureView(BuildStatusView);
        }

        private void BuildStatusView(Panel panel)
        {
            var layout = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false
            };

            layout.Controls.Add(MakeHeading("Chat status"));
            layout.Controls.Add(MakeMutedLabel("The status message shown in your friends list. Long messages can bug out your chat.", 420));

            var statusTextBox = new TextBox
            {
                Multiline = true,
                Size = new Size(420, 80),
                MaxLength = 300,
                Margin = new Padding(0, 0, 0, 12)
            };
            Theme.StyleTextBox(statusTextBox);

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(0)
            };

            var setButton = new Button { Text = "Set status", Size = new Size(206, 34), Margin = new Padding(0, 0, 8, 0) };
            Theme.StylePrimaryButton(setButton);
            setButton.Click += async (s, e) => await SendStatusMessage(statusTextBox.Text);

            var clearButton = new Button { Text = "Clear status", Size = new Size(206, 34), Margin = new Padding(0) };
            Theme.StyleButton(clearButton);
            clearButton.Click += async (s, e) =>
            {
                statusTextBox.Text = "";
                await SendStatusMessage("");
            };

            buttons.Controls.Add(setButton);
            buttons.Controls.Add(clearButton);

            layout.Controls.Add(statusTextBox);
            layout.Controls.Add(buttons);
            panel.Controls.Add(layout);
        }

        private async System.Threading.Tasks.Task SendStatusMessage(string text)
        {
            if (!EnsureConnected()) return;

            var body = new JsonObject { ["statusMessage"] = text };
            try
            {
                var (status, _) = await league.Put("/lol-chat/v1/me/", SimpleJson.SerializeObject(body));
                if (status >= 200 && status < 300)
                    SetStatus(text.Length == 0 ? "Chat status cleared." : "Chat status updated.", StatusKind.Success);
                else
                    SetStatus("Setting the chat status failed (HTTP " + status + ").", StatusKind.Error);
            }
            catch (Exception ex)
            {
                SetStatus("Setting the chat status failed: " + ex.Message, StatusKind.Error);
            }
        }

        // ---------- API console ----------

        private void customApiButton_Click(object sender, EventArgs e)
        {
            ShowFeatureView(BuildApiConsoleView);
        }

        private void BuildApiConsoleView(Panel panel)
        {
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                BackColor = Theme.Canvas
            };

            var bodyTextBox = MakeConsoleTextBox(readOnly: false);
            bodyTextBox.Text = "{\r\n    \"\": \"\"\r\n}";
            split.Panel1.Controls.Add(bodyTextBox);
            split.Panel1.Controls.Add(MakeConsoleLabel("Body (optional)"));

            var responseTextBox = MakeConsoleTextBox(readOnly: true);
            split.Panel2.Controls.Add(responseTextBox);
            split.Panel2.Controls.Add(MakeConsoleLabel("Response"));

            var topBar = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 40,
                ColumnCount = 3
            };
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));

            var methodBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) };
            Theme.StyleComboBox(methodBox);
            methodBox.Items.AddRange(new object[] { "GET", "POST", "PUT", "PATCH", "DELETE" });
            methodBox.SelectedIndex = 0;

            var endpointTextBox = new TextBox
            {
                Text = "/lol-summoner/v1/current-summoner",
                Font = Theme.MonoFont,
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0, 0, 8, 0)
            };
            Theme.StyleTextBox(endpointTextBox);
            endpointTextBox.Font = Theme.MonoFont;

            var sendButton = new Button { Text = "Send", Size = new Size(80, 28), Anchor = AnchorStyles.Left, Margin = new Padding(0) };
            Theme.StylePrimaryButton(sendButton);
            sendButton.Click += async (s, e) =>
                await SendConsoleRequest((string)methodBox.SelectedItem, endpointTextBox, bodyTextBox, responseTextBox);

            topBar.Controls.Add(methodBox, 0, 0);
            topBar.Controls.Add(endpointTextBox, 1, 0);
            topBar.Controls.Add(sendButton, 2, 0);

            panel.Controls.Add(split);
            panel.Controls.Add(topBar);
        }

        private Label MakeConsoleLabel(string text)
        {
            return new Label
            {
                Text = text,
                Dock = DockStyle.Top,
                Height = 22,
                Font = Theme.SectionFont,
                ForeColor = Theme.TextMuted,
                TextAlign = ContentAlignment.BottomLeft
            };
        }

        private TextBox MakeConsoleTextBox(bool readOnly)
        {
            var textBox = new TextBox
            {
                Multiline = true,
                ReadOnly = readOnly,
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                AcceptsReturn = true,
                AcceptsTab = true
            };
            Theme.StyleTextBox(textBox);
            textBox.Font = Theme.MonoFont;
            if (readOnly) textBox.BackColor = Theme.Surface;
            return textBox;
        }

        private async System.Threading.Tasks.Task SendConsoleRequest(string method, TextBox endpointTextBox, TextBox bodyTextBox, TextBox responseTextBox)
        {
            if (!EnsureConnected()) return;

            string endpoint = endpointTextBox.Text.Trim();
            if (endpoint.Length == 0)
            {
                responseTextBox.Text = "Enter an endpoint first.";
                return;
            }
            if (!endpoint.StartsWith("/")) endpoint = "/" + endpoint;

            // net48 refuses to send a content body with GET.
            string body = method == "GET" ? null : bodyTextBox.Text;
            if (!string.IsNullOrWhiteSpace(body) && !SimpleJson.TryDeserializeObject(body, out _))
            {
                responseTextBox.Text = "Invalid JSON body.";
                return;
            }

            try
            {
                responseTextBox.Text = "Sending...";
                var (status, content) = await league.Request(method, endpoint, body);
                responseTextBox.Text = "HTTP " + status + "\r\n\r\n" + JsonPretty.Format(content);
            }
            catch (Exception ex)
            {
                responseTextBox.Text = ex.Message;
                SetStatus("Request failed: " + ex.Message, StatusKind.Error);
            }
        }
    }
}
