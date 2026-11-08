using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PoroTool
{
    /// <summary>
    /// Update notifications: checks GitHub shortly after start and then every
    /// few hours. When a newer release is out it says so once, and OK opens
    /// the latest release page. A link in the status bar stays up afterwards.
    /// </summary>
    public partial class MainForm
    {
        private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);

        private Timer updateTimer;
        private LinkLabel updateLink;
        private string updateNotifiedTag;
        private bool updateCheckRunning;

        private void InitUpdates()
        {
            Text = "Poro Tool " + UpdateChecker.CurrentVersion;

            updateLink = new LinkLabel
            {
                AutoSize = true,
                Dock = DockStyle.Right,
                Visible = false,
                Font = Theme.StatusFont,
                LinkColor = Theme.Pink,
                ActiveLinkColor = Theme.Pink,
                VisitedLinkColor = Theme.Pink,
                LinkBehavior = LinkBehavior.HoverUnderline,
                TextAlign = System.Drawing.ContentAlignment.MiddleRight,
                Padding = new Padding(0, 0, 12, 0),
                BackColor = Theme.Sidebar
            };
            updateLink.LinkClicked += (s, e) => OpenLatestRelease();
            statusStrip.Controls.Add(updateLink);
            // Dock order: divider (top) first, then this link (right), then the message fills the rest.
            statusStrip.Controls.SetChildIndex(updateLink, 1);

            updateTimer = new Timer { Interval = 5000 };
            updateTimer.Tick += async (s, e) =>
            {
                updateTimer.Interval = (int)UpdateCheckInterval.TotalMilliseconds;
                await CheckForUpdatesAsync();
            };
            updateTimer.Start();
        }

        private async Task CheckForUpdatesAsync()
        {
            if (updateCheckRunning) return;
            updateCheckRunning = true;
            try
            {
                var release = await UpdateChecker.GetNewerReleaseAsync();
                if (release == null || IsDisposed) return;

                updateLink.Text = "Update available: " + release.Name;
                updateLink.Visible = true;

                // One prompt per new version per session; the link stays.
                if (release.Tag == updateNotifiedTag) return;
                updateNotifiedTag = release.Tag;

                var answer = MessageBox.Show(this,
                    "A new version of Poro Tool is out: " + release.Name + " (you have " + UpdateChecker.CurrentVersion + ").\n\n" +
                    "Click OK to open the download page on GitHub.",
                    "Poro Tool update", MessageBoxButtons.OKCancel, MessageBoxIcon.Information);
                if (answer == DialogResult.OK) OpenLatestRelease();
            }
            catch (Exception ex)
            {
                // Offline or GitHub unreachable: try again at the next interval.
                Log.Write("update check failed: " + ex.Message);
            }
            finally
            {
                updateCheckRunning = false;
            }
        }

        private void OpenLatestRelease()
        {
            try
            {
                Process.Start(UpdateChecker.LatestReleasePage);
            }
            catch (Exception ex)
            {
                SetStatus("Couldn't open the browser: " + ex.Message, StatusKind.Error);
            }
        }
    }
}
