using System;
using System.Threading;
using System.Windows.Forms;

namespace RoboCopyPaste
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, "Local\\RoboCopyPaste.SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("RoboCopy Paste 已经在运行，请查看系统托盘。", "RoboCopy Paste",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool startHidden = args != null && Array.Exists(args,
                    delegate(string value) { return string.Equals(value, "--startup", StringComparison.OrdinalIgnoreCase); });
                Application.Run(new MainForm(startHidden));
            }
        }
    }
}
