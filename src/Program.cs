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
            if (HasArgument(args, "--install-context-menu"))
            {
                ContextMenuIntegration.Install();
                return;
            }
            if (HasArgument(args, "--uninstall-context-menu"))
            {
                ContextMenuIntegration.Uninstall();
                return;
            }

            string pasteTarget = ArgumentValue(args, "--paste-target");
            if (!string.IsNullOrWhiteSpace(pasteTarget))
            {
                ConfigureApplication();
                Application.Run(new MainForm(false, pasteTarget, true));
                return;
            }

            bool created;
            using (var mutex = new Mutex(true, "Local\\RoboCopyPaste.SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("RoboCopy Paste 已经在运行，请查看系统托盘。", "RoboCopy Paste",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                ConfigureApplication();
                bool startHidden = HasArgument(args, "--startup");
                Application.Run(new MainForm(startHidden, null, false));
            }
        }

        private static void ConfigureApplication()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
        }

        private static bool HasArgument(string[] args, string name)
        {
            return args != null && Array.Exists(args,
                delegate(string value) { return string.Equals(value, name, StringComparison.OrdinalIgnoreCase); });
        }

        private static string ArgumentValue(string[] args, string name)
        {
            if (args == null) return null;
            for (int index = 0; index < args.Length - 1; index++)
                if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
            return null;
        }
    }
}
