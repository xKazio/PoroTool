using System;
using System.Net;
using System.Windows.Forms;

namespace PoroTool
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            // net48 allows only 2 concurrent connections per host by default,
            // which would serialize the CDN thumbnail downloads.
            ServicePointManager.DefaultConnectionLimit = 64;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
