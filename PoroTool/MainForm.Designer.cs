namespace PoroTool
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }

            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.sidebar = new System.Windows.Forms.Panel();
            this.sidebarLayout = new System.Windows.Forms.FlowLayoutPanel();
            this.lootSectionLabel = new System.Windows.Forms.Label();
            this.loadChampionsButton = new System.Windows.Forms.Button();
            this.loadSkinsButton = new System.Windows.Forms.Button();
            this.loadEmotesButton = new System.Windows.Forms.Button();
            this.loadWardsButton = new System.Windows.Forms.Button();
            this.loadIconsButton = new System.Windows.Forms.Button();
            this.loadCompanionsButton = new System.Windows.Forms.Button();
            this.loadEternalsButton = new System.Windows.Forms.Button();
            this.loadChestsButton = new System.Windows.Forms.Button();
            this.profileSectionLabel = new System.Windows.Forms.Label();
            this.chatRankButton = new System.Windows.Forms.Button();
            this.backgroundButton = new System.Windows.Forms.Button();
            this.statusButton = new System.Windows.Forms.Button();
            this.purchaseDatesButton = new System.Windows.Forms.Button();
            this.customApiButton = new System.Windows.Forms.Button();
            this.liveSectionLabel = new System.Windows.Forms.Label();
            this.lobbyRevealButton = new System.Windows.Forms.Button();
            this.processSectionLabel = new System.Windows.Forms.Label();
            this.processOptions = new System.Windows.Forms.FlowLayoutPanel();
            this.processButton = new System.Windows.Forms.Button();
            this.featurePanel = new System.Windows.Forms.Panel();
            this.sidebarBottom = new System.Windows.Forms.FlowLayoutPanel();
            this.removeTokensButton = new System.Windows.Forms.Button();
            this.socialsPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.discordButton = new System.Windows.Forms.Button();
            this.githubButton = new System.Windows.Forms.Button();
            this.legalNoteButton = new System.Windows.Forms.Button();
            this.outputPanel = new System.Windows.Forms.FlowLayoutPanel();
            this.statusStrip = new System.Windows.Forms.Panel();
            this.messageLabel = new System.Windows.Forms.Label();
            this.statusDivider = new System.Windows.Forms.Panel();
            this.sidebar.SuspendLayout();
            this.sidebarLayout.SuspendLayout();
            this.sidebarBottom.SuspendLayout();
            this.statusStrip.SuspendLayout();
            this.SuspendLayout();
            //
            // sidebar
            //
            this.sidebar.Controls.Add(this.sidebarLayout);
            this.sidebar.Controls.Add(this.sidebarBottom);
            this.sidebar.Dock = System.Windows.Forms.DockStyle.Left;
            this.sidebar.Name = "sidebar";
            this.sidebar.Padding = new System.Windows.Forms.Padding(12);
            this.sidebar.Size = new System.Drawing.Size(220, 610);
            this.sidebar.TabIndex = 0;
            //
            // sidebarLayout
            //
            this.sidebarLayout.Controls.Add(this.lootSectionLabel);
            this.sidebarLayout.Controls.Add(this.loadChampionsButton);
            this.sidebarLayout.Controls.Add(this.loadSkinsButton);
            this.sidebarLayout.Controls.Add(this.loadEmotesButton);
            this.sidebarLayout.Controls.Add(this.loadWardsButton);
            this.sidebarLayout.Controls.Add(this.loadIconsButton);
            this.sidebarLayout.Controls.Add(this.loadCompanionsButton);
            this.sidebarLayout.Controls.Add(this.loadEternalsButton);
            this.sidebarLayout.Controls.Add(this.loadChestsButton);
            this.sidebarLayout.Controls.Add(this.profileSectionLabel);
            this.sidebarLayout.Controls.Add(this.chatRankButton);
            this.sidebarLayout.Controls.Add(this.backgroundButton);
            this.sidebarLayout.Controls.Add(this.statusButton);
            this.sidebarLayout.Controls.Add(this.purchaseDatesButton);
            this.sidebarLayout.Controls.Add(this.customApiButton);
            this.sidebarLayout.Controls.Add(this.liveSectionLabel);
            this.sidebarLayout.Controls.Add(this.lobbyRevealButton);
            this.sidebarLayout.Controls.Add(this.processSectionLabel);
            this.sidebarLayout.Controls.Add(this.processOptions);
            this.sidebarLayout.Controls.Add(this.processButton);
            this.sidebarLayout.AutoScroll = true;
            this.sidebarLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.sidebarLayout.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.sidebarLayout.Margin = new System.Windows.Forms.Padding(0);
            this.sidebarLayout.Name = "sidebarLayout";
            this.sidebarLayout.WrapContents = false;
            this.sidebarLayout.TabIndex = 0;
            //
            // lootSectionLabel
            //
            this.lootSectionLabel.AutoSize = true;
            this.lootSectionLabel.Margin = new System.Windows.Forms.Padding(2, 0, 0, 4);
            this.lootSectionLabel.Name = "lootSectionLabel";
            this.lootSectionLabel.Text = "LOOT";
            //
            // loadChampionsButton
            //
            this.loadChampionsButton.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            this.loadChampionsButton.Name = "loadChampionsButton";
            this.loadChampionsButton.Size = new System.Drawing.Size(196, 30);
            this.loadChampionsButton.TabIndex = 0;
            this.loadChampionsButton.Text = "Champions";
            this.loadChampionsButton.Click += new System.EventHandler(this.loadChampionsButton_Click);
            //
            // loadSkinsButton
            //
            this.loadSkinsButton.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            this.loadSkinsButton.Name = "loadSkinsButton";
            this.loadSkinsButton.Size = new System.Drawing.Size(196, 30);
            this.loadSkinsButton.TabIndex = 1;
            this.loadSkinsButton.Text = "Skins";
            this.loadSkinsButton.Click += new System.EventHandler(this.loadSkinsButton_Click);
            //
            // loadEmotesButton
            //
            this.loadEmotesButton.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            this.loadEmotesButton.Name = "loadEmotesButton";
            this.loadEmotesButton.Size = new System.Drawing.Size(196, 30);
            this.loadEmotesButton.TabIndex = 2;
            this.loadEmotesButton.Text = "Emotes";
            this.loadEmotesButton.Click += new System.EventHandler(this.loadEmotesButton_Click);
            //
            // loadWardsButton
            //
            this.loadWardsButton.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            this.loadWardsButton.Name = "loadWardsButton";
            this.loadWardsButton.Size = new System.Drawing.Size(196, 30);
            this.loadWardsButton.TabIndex = 3;
            this.loadWardsButton.Text = "Wards";
            this.loadWardsButton.Click += new System.EventHandler(this.loadWardsButton_Click);
            //
            // loadIconsButton
            //
            this.loadIconsButton.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            this.loadIconsButton.Name = "loadIconsButton";
            this.loadIconsButton.Size = new System.Drawing.Size(196, 30);
            this.loadIconsButton.TabIndex = 4;
            this.loadIconsButton.Text = "Icons";
            this.loadIconsButton.Click += new System.EventHandler(this.loadIconsButton_Click);
            //
            // loadCompanionsButton
            //
            this.loadCompanionsButton.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            this.loadCompanionsButton.Name = "loadCompanionsButton";
            this.loadCompanionsButton.Size = new System.Drawing.Size(196, 30);
            this.loadCompanionsButton.TabIndex = 5;
            this.loadCompanionsButton.Text = "Companions";
            this.loadCompanionsButton.Click += new System.EventHandler(this.loadCompanionsButton_Click);
            //
            // loadEternalsButton
            //
            this.loadEternalsButton.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            this.loadEternalsButton.Name = "loadEternalsButton";
            this.loadEternalsButton.Size = new System.Drawing.Size(196, 30);
            this.loadEternalsButton.TabIndex = 6;
            this.loadEternalsButton.Text = "Eternals";
            this.loadEternalsButton.Click += new System.EventHandler(this.loadEternalsButton_Click);
            //
            // loadChestsButton
            //
            this.loadChestsButton.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            this.loadChestsButton.Name = "loadChestsButton";
            this.loadChestsButton.Size = new System.Drawing.Size(196, 30);
            this.loadChestsButton.TabIndex = 7;
            this.loadChestsButton.Text = "Chests";
            this.loadChestsButton.Click += new System.EventHandler(this.loadChestsButton_Click);
            //
            // profileSectionLabel
            //
            this.profileSectionLabel.AutoSize = true;
            this.profileSectionLabel.Margin = new System.Windows.Forms.Padding(2, 16, 0, 4);
            this.profileSectionLabel.Name = "profileSectionLabel";
            this.profileSectionLabel.Text = "PROFILE";
            //
            // chatRankButton
            //
            this.chatRankButton.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            this.chatRankButton.Name = "chatRankButton";
            this.chatRankButton.Size = new System.Drawing.Size(196, 30);
            this.chatRankButton.TabIndex = 12;
            this.chatRankButton.Text = "Chat rank";
            this.chatRankButton.Click += new System.EventHandler(this.chatRankButton_Click);
            //
            // backgroundButton
            //
            this.backgroundButton.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            this.backgroundButton.Name = "backgroundButton";
            this.backgroundButton.Size = new System.Drawing.Size(196, 30);
            this.backgroundButton.TabIndex = 13;
            this.backgroundButton.Text = "Profile background";
            this.backgroundButton.Click += new System.EventHandler(this.backgroundButton_Click);
            //
            // statusButton
            //
            this.statusButton.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            this.statusButton.Name = "statusButton";
            this.statusButton.Size = new System.Drawing.Size(196, 30);
            this.statusButton.TabIndex = 14;
            this.statusButton.Text = "Chat status";
            this.statusButton.Click += new System.EventHandler(this.statusButton_Click);
            //
            // purchaseDatesButton
            //
            this.purchaseDatesButton.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            this.purchaseDatesButton.Name = "purchaseDatesButton";
            this.purchaseDatesButton.Size = new System.Drawing.Size(196, 30);
            this.purchaseDatesButton.TabIndex = 15;
            this.purchaseDatesButton.Text = "Purchase dates";
            this.purchaseDatesButton.Click += new System.EventHandler(this.purchaseDatesButton_Click);
            //
            // customApiButton
            //
            this.customApiButton.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            this.customApiButton.Name = "customApiButton";
            this.customApiButton.Size = new System.Drawing.Size(196, 30);
            this.customApiButton.TabIndex = 16;
            this.customApiButton.Text = "API console";
            this.customApiButton.Click += new System.EventHandler(this.customApiButton_Click);
            //
            // liveSectionLabel
            //
            this.liveSectionLabel.AutoSize = true;
            this.liveSectionLabel.Margin = new System.Windows.Forms.Padding(2, 16, 0, 4);
            this.liveSectionLabel.Name = "liveSectionLabel";
            this.liveSectionLabel.Text = "LIVE";
            //
            // lobbyRevealButton
            //
            this.lobbyRevealButton.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);
            this.lobbyRevealButton.Name = "lobbyRevealButton";
            this.lobbyRevealButton.Size = new System.Drawing.Size(196, 30);
            this.lobbyRevealButton.TabIndex = 19;
            this.lobbyRevealButton.Text = "Lobby reveal + auto accept";
            this.lobbyRevealButton.Click += new System.EventHandler(this.lobbyRevealButton_Click);
            //
            // processSectionLabel
            //
            this.processSectionLabel.AutoSize = true;
            this.processSectionLabel.Margin = new System.Windows.Forms.Padding(2, 16, 0, 4);
            this.processSectionLabel.Name = "processSectionLabel";
            this.processSectionLabel.Text = "PROCESS";
            //
            // processOptions
            //
            this.processOptions.AutoSize = true;
            this.processOptions.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.processOptions.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.processOptions.Margin = new System.Windows.Forms.Padding(0);
            this.processOptions.Name = "processOptions";
            this.processOptions.Size = new System.Drawing.Size(196, 68);
            this.processOptions.TabIndex = 8;
            this.processOptions.WrapContents = false;
            //
            // processButton
            //
            this.processButton.Margin = new System.Windows.Forms.Padding(0, 8, 0, 0);
            this.processButton.Name = "processButton";
            this.processButton.Size = new System.Drawing.Size(196, 40);
            this.processButton.TabIndex = 9;
            this.processButton.Text = "Process";
            this.processButton.Click += new System.EventHandler(this.processButton_Click);
            //
            // sidebarBottom
            //
            this.sidebarBottom.Controls.Add(this.removeTokensButton);
            this.sidebarBottom.Controls.Add(this.socialsPanel);
            this.sidebarBottom.Controls.Add(this.legalNoteButton);
            this.sidebarBottom.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.sidebarBottom.FlowDirection = System.Windows.Forms.FlowDirection.TopDown;
            this.sidebarBottom.Margin = new System.Windows.Forms.Padding(0);
            this.sidebarBottom.Name = "sidebarBottom";
            this.sidebarBottom.Size = new System.Drawing.Size(196, 102);
            this.sidebarBottom.WrapContents = false;
            this.sidebarBottom.TabIndex = 1;
            //
            // removeTokensButton
            //
            this.removeTokensButton.Margin = new System.Windows.Forms.Padding(0, 0, 0, 6);
            this.removeTokensButton.Name = "removeTokensButton";
            this.removeTokensButton.Size = new System.Drawing.Size(196, 34);
            this.removeTokensButton.TabIndex = 10;
            this.removeTokensButton.Text = "Remove challenge tokens";
            this.removeTokensButton.Click += new System.EventHandler(this.removeTokensButton_Click);
            //
            // socialsPanel
            //
            this.socialsPanel.Controls.Add(this.discordButton);
            this.socialsPanel.Controls.Add(this.githubButton);
            this.socialsPanel.Margin = new System.Windows.Forms.Padding(0, 0, 0, 6);
            this.socialsPanel.Name = "socialsPanel";
            this.socialsPanel.Size = new System.Drawing.Size(196, 30);
            this.socialsPanel.WrapContents = false;
            //
            // discordButton
            //
            this.discordButton.Margin = new System.Windows.Forms.Padding(0, 0, 6, 0);
            this.discordButton.Name = "discordButton";
            this.discordButton.Size = new System.Drawing.Size(95, 30);
            this.discordButton.TabIndex = 17;
            this.discordButton.Text = "Discord";
            this.discordButton.Click += new System.EventHandler(this.discordButton_Click);
            //
            // githubButton
            //
            this.githubButton.Margin = new System.Windows.Forms.Padding(0);
            this.githubButton.Name = "githubButton";
            this.githubButton.Size = new System.Drawing.Size(95, 30);
            this.githubButton.TabIndex = 18;
            this.githubButton.Text = "GitHub";
            this.githubButton.Click += new System.EventHandler(this.githubButton_Click);
            //
            // legalNoteButton
            //
            this.legalNoteButton.Margin = new System.Windows.Forms.Padding(0);
            this.legalNoteButton.Name = "legalNoteButton";
            this.legalNoteButton.Size = new System.Drawing.Size(196, 26);
            this.legalNoteButton.TabIndex = 11;
            this.legalNoteButton.Text = "Legal note";
            this.legalNoteButton.Click += new System.EventHandler(this.legalNoteButton_Click);
            //
            // outputPanel
            //
            this.outputPanel.AutoScroll = true;
            this.outputPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.outputPanel.Name = "outputPanel";
            this.outputPanel.Padding = new System.Windows.Forms.Padding(16);
            this.outputPanel.TabIndex = 1;
            //
            // featurePanel
            //
            this.featurePanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.featurePanel.Name = "featurePanel";
            this.featurePanel.Padding = new System.Windows.Forms.Padding(16);
            this.featurePanel.TabIndex = 3;
            this.featurePanel.Visible = false;
            //
            // statusStrip
            //
            this.statusStrip.Controls.Add(this.messageLabel);
            this.statusStrip.Controls.Add(this.statusDivider);
            this.statusStrip.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.statusStrip.Name = "statusStrip";
            this.statusStrip.Size = new System.Drawing.Size(900, 30);
            this.statusStrip.TabIndex = 2;
            //
            // messageLabel
            //
            this.messageLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.messageLabel.Name = "messageLabel";
            this.messageLabel.Padding = new System.Windows.Forms.Padding(12, 0, 0, 0);
            this.messageLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            this.messageLabel.TabIndex = 0;
            //
            // statusDivider
            //
            this.statusDivider.Dock = System.Windows.Forms.DockStyle.Top;
            this.statusDivider.Name = "statusDivider";
            this.statusDivider.Size = new System.Drawing.Size(900, 1);
            this.statusDivider.TabIndex = 1;
            //
            // MainForm
            //
            this.ClientSize = new System.Drawing.Size(900, 930);
            this.Controls.Add(this.outputPanel);
            this.Controls.Add(this.featurePanel);
            this.Controls.Add(this.sidebar);
            this.Controls.Add(this.statusStrip);
            this.MinimumSize = new System.Drawing.Size(760, 720);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Poro Tool";
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.sidebar.ResumeLayout(false);
            this.sidebarLayout.ResumeLayout(false);
            this.sidebarLayout.PerformLayout();
            this.sidebarBottom.ResumeLayout(false);
            this.statusStrip.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel sidebar;
        private System.Windows.Forms.FlowLayoutPanel sidebarLayout;
        private System.Windows.Forms.Label lootSectionLabel;
        private System.Windows.Forms.Button loadChampionsButton;
        private System.Windows.Forms.Button loadSkinsButton;
        private System.Windows.Forms.Button loadEmotesButton;
        private System.Windows.Forms.Button loadWardsButton;
        private System.Windows.Forms.Button loadIconsButton;
        private System.Windows.Forms.Button loadCompanionsButton;
        private System.Windows.Forms.Button loadEternalsButton;
        private System.Windows.Forms.Button loadChestsButton;
        private System.Windows.Forms.Label profileSectionLabel;
        private System.Windows.Forms.Button chatRankButton;
        private System.Windows.Forms.Button backgroundButton;
        private System.Windows.Forms.Button statusButton;
        private System.Windows.Forms.Button purchaseDatesButton;
        private System.Windows.Forms.Button customApiButton;
        private System.Windows.Forms.Label liveSectionLabel;
        private System.Windows.Forms.Button lobbyRevealButton;
        private System.Windows.Forms.Label processSectionLabel;
        private System.Windows.Forms.FlowLayoutPanel processOptions;
        private System.Windows.Forms.Button processButton;
        private System.Windows.Forms.FlowLayoutPanel sidebarBottom;
        private System.Windows.Forms.Button removeTokensButton;
        private System.Windows.Forms.FlowLayoutPanel socialsPanel;
        private System.Windows.Forms.Button discordButton;
        private System.Windows.Forms.Button githubButton;
        private System.Windows.Forms.Button legalNoteButton;
        private System.Windows.Forms.FlowLayoutPanel outputPanel;
        private System.Windows.Forms.Panel featurePanel;
        private System.Windows.Forms.Panel statusStrip;
        private System.Windows.Forms.Label messageLabel;
        private System.Windows.Forms.Panel statusDivider;
    }
}
