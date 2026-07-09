using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace PoroTool
{
    /// <summary>
    /// Profile background view: a searchable grid of champions, then their
    /// skin splashes; clicking a splash makes it the profile background.
    /// Thumbnails stream in from Data Dragon while the grid stays responsive.
    /// </summary>
    public partial class MainForm
    {
        private BufferedFlowLayoutPanel backgroundGrid;
        private TextBox backgroundSearchBox;
        private Button backgroundBackButton;
        private Label backgroundTitleLabel;
        private System.Windows.Forms.Timer backgroundSearchTimer;

        private string backgroundVersion;
        private List<DataDragon.ChampionEntry> backgroundChampions;
        private DataDragon.ChampionEntry backgroundSelectedChampion; // null while picking a champion
        private CancellationTokenSource backgroundGridCts;

        private void backgroundButton_Click(object sender, EventArgs e)
        {
            ShowFeatureView(BuildBackgroundView);
        }

        private void BuildBackgroundView(Panel panel)
        {
            backgroundSelectedChampion = null;
            backgroundChampions = null;

            backgroundGrid = new BufferedFlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Theme.Canvas
            };

            var topBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 38,
                WrapContents = false
            };

            backgroundSearchBox = new TextBox { Width = 220, Margin = new Padding(0, 4, 8, 0) };
            Theme.StyleTextBox(backgroundSearchBox);
            SetPlaceholder(backgroundSearchBox, "Search...");
            backgroundSearchBox.TextChanged += (s, e) =>
            {
                backgroundSearchTimer.Stop();
                backgroundSearchTimer.Start();
            };

            backgroundBackButton = new Button
            {
                Text = "← Champions",
                Size = new Size(120, 28),
                Margin = new Padding(0, 2, 8, 0),
                Visible = false
            };
            Theme.StyleButton(backgroundBackButton);
            backgroundBackButton.Click += (s, e) =>
            {
                ClearBackgroundSearchSilently();
                ShowChampionGrid("");
            };

            backgroundTitleLabel = new Label
            {
                Text = "Pick a champion",
                AutoSize = true,
                Font = Theme.RowHeaderFont,
                ForeColor = Theme.TextMuted,
                Margin = new Padding(4, 8, 0, 0)
            };

            topBar.Controls.Add(backgroundSearchBox);
            topBar.Controls.Add(backgroundBackButton);
            topBar.Controls.Add(backgroundTitleLabel);

            if (backgroundSearchTimer == null)
            {
                backgroundSearchTimer = new System.Windows.Forms.Timer { Interval = 250 };
                backgroundSearchTimer.Tick += (s, e) =>
                {
                    backgroundSearchTimer.Stop();
                    if (backgroundGrid == null || backgroundGrid.IsDisposed) return;

                    if (backgroundSelectedChampion == null) ShowChampionGrid(backgroundSearchBox.Text);
                    else ShowSkinGrid(backgroundSelectedChampion, backgroundSearchBox.Text);
                };
            }

            panel.Controls.Add(backgroundGrid);
            panel.Controls.Add(topBar);

            InitBackgroundView(viewCts.Token);
        }

        private async void InitBackgroundView(CancellationToken token)
        {
            SetStatus("Loading champions from Data Dragon...");
            try
            {
                backgroundVersion = await DataDragon.GetVersionAsync();
                backgroundChampions = await DataDragon.GetChampionsAsync();
            }
            catch (Exception ex)
            {
                SetStatus("Loading champion data failed: " + ex.Message, StatusKind.Error);
                return;
            }

            if (token.IsCancellationRequested || backgroundGrid.IsDisposed) return;
            ShowChampionGrid("");
            SetStatus("Pick a champion, then a skin to use as your profile background.");
        }

        private CancellationToken NewBackgroundGridToken()
        {
            backgroundGridCts?.Cancel();
            backgroundGridCts = CancellationTokenSource.CreateLinkedTokenSource(viewCts.Token);
            return backgroundGridCts.Token;
        }

        private void ClearBackgroundSearchSilently()
        {
            backgroundSearchBox.Text = "";
            backgroundSearchTimer.Stop();
        }

        private void ClearBackgroundGrid()
        {
            while (backgroundGrid.Controls.Count > 0)
            {
                var control = backgroundGrid.Controls[0];
                backgroundGrid.Controls.RemoveAt(0);
                control.Dispose();
            }
            // Safe now: every tile that borrowed a splash is disposed and the
            // grid token is cancelled, so late loads won't touch freed images.
            ImageCache.ClearSplashCache();
        }

        private void ShowChampionGrid(string filter)
        {
            if (backgroundChampions == null) return;

            backgroundSelectedChampion = null;
            backgroundBackButton.Visible = false;
            backgroundTitleLabel.Text = "Pick a champion";

            var token = NewBackgroundGridToken();
            filter = filter.Trim();

            var tiles = new List<Control>();
            var loads = new List<Action>();
            foreach (var champion in backgroundChampions)
            {
                // Alias matters too: Wukong's alias is MonkeyKing.
                if (filter.Length > 0 &&
                    champion.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    champion.Alias.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                var captured = champion;
                var tile = MakeTile(104, 132, 96, 96, champion.Name,
                                    () => OpenSkinGrid(captured), out var pictureBox);
                tiles.Add(tile);
                loads.Add(() => LoadTileImage(pictureBox, DataDragon.ChampionIconUrl(backgroundVersion, captured.Alias),
                                              96, 96, splash: false, token));
            }

            backgroundGrid.SuspendLayout();
            ClearBackgroundGrid();
            backgroundGrid.Controls.AddRange(tiles.ToArray());
            backgroundGrid.ResumeLayout();

            foreach (var load in loads) load();
        }

        private void OpenSkinGrid(DataDragon.ChampionEntry champion)
        {
            ClearBackgroundSearchSilently();
            ShowSkinGrid(champion, "");
        }

        private async void ShowSkinGrid(DataDragon.ChampionEntry champion, string filter)
        {
            backgroundSelectedChampion = champion;
            backgroundBackButton.Visible = true;
            backgroundTitleLabel.Text = champion.Name;

            List<DataDragon.SkinEntry> skins;
            try
            {
                skins = await DataDragon.GetSkinsAsync(champion);
            }
            catch (Exception ex)
            {
                SetStatus("Loading skins failed: " + ex.Message, StatusKind.Error);
                return;
            }

            if (backgroundGrid.IsDisposed || backgroundSelectedChampion != champion) return;

            var token = NewBackgroundGridToken();
            filter = filter.Trim();

            var tiles = new List<Control>();
            var loads = new List<Action>();
            foreach (var skin in skins)
            {
                if (filter.Length > 0 && skin.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                var captured = skin;
                var tile = MakeTile(264, 186, 256, 151, skin.Name,
                                    () => SetProfileBackground(captured), out var pictureBox);
                tiles.Add(tile);
                loads.Add(() => LoadTileImage(pictureBox, DataDragon.SplashUrl(champion.Alias, captured.Num),
                                              256, 151, splash: true, token));
            }

            backgroundGrid.SuspendLayout();
            ClearBackgroundGrid();
            backgroundGrid.Controls.AddRange(tiles.ToArray());
            backgroundGrid.ResumeLayout();

            foreach (var load in loads) load();
        }

        private Control MakeTile(int width, int height, int imageWidth, int imageHeight, string caption, Action onClick, out PictureBox pictureBox)
        {
            var tile = new Panel
            {
                Size = new Size(width, height),
                Margin = new Padding(4),
                Cursor = Cursors.Hand
            };

            pictureBox = new PictureBox
            {
                Size = new Size(imageWidth, imageHeight),
                Location = new Point((width - imageWidth) / 2, 4),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Theme.SurfaceHover,
                Cursor = Cursors.Hand
            };

            var label = new Label
            {
                Text = caption,
                Location = new Point(0, imageHeight + 8),
                Size = new Size(width, height - imageHeight - 8),
                TextAlign = ContentAlignment.TopCenter,
                AutoEllipsis = true,
                Font = Theme.StatusFont,
                ForeColor = Theme.TextPrimary,
                Cursor = Cursors.Hand
            };

            tile.Controls.Add(pictureBox);
            tile.Controls.Add(label);

            EventHandler clickHandler = (s, e) => onClick();
            tile.Click += clickHandler;
            pictureBox.Click += clickHandler;
            label.Click += clickHandler;

            return tile;
        }

        private async void LoadTileImage(PictureBox pictureBox, string url, int width, int height, bool splash, CancellationToken token)
        {
            Image image;
            try
            {
                image = splash
                    ? await ImageCache.GetSplashAsync(url, width, height, token)
                    : await ImageCache.GetIconAsync(url, width, height, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (image == null || token.IsCancellationRequested || pictureBox.IsDisposed) return;
            pictureBox.Image = image;
        }

        private async void SetProfileBackground(DataDragon.SkinEntry skin)
        {
            if (!EnsureConnected()) return;

            var body = new JsonObject { ["key"] = "backgroundSkinId", ["value"] = skin.Id };
            string json = SimpleJson.SerializeObject(body);

            try
            {
                var (status, _) = await league.Request("POST", "/lol-summoner/v1/current-summoner/summoner-profile/", json);
                // Some client builds want PUT for this endpoint.
                if (status == 405)
                    (status, _) = await league.Put("/lol-summoner/v1/current-summoner/summoner-profile/", json);

                if (status >= 200 && status < 300)
                    SetStatus("Profile background set to " + skin.Name + ".", StatusKind.Success);
                else
                    SetStatus("Setting the background failed (HTTP " + status + ").", StatusKind.Error);
            }
            catch (Exception ex)
            {
                SetStatus("Setting the background failed: " + ex.Message, StatusKind.Error);
            }
        }
    }
}
