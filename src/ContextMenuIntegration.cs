using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RoboCopyPaste
{
    internal static class ContextMenuIntegration
    {
        private const string MenuName = "RoboCopyPaste";
        private static readonly string[] MenuRoots =
        {
            @"Software\Classes\Directory\Background\shell\" + MenuName,
            @"Software\Classes\Directory\shell\" + MenuName,
            @"Software\Classes\Drive\shell\" + MenuName,
            @"Software\Classes\DesktopBackground\Shell\" + MenuName
        };

        public static void Apply(bool enabled)
        {
            if (enabled) Install();
            else Uninstall();
        }

        public static void Install()
        {
            string executable = Application.ExecutablePath;
            InstallVerb(MenuRoots[0], "%V", executable);
            InstallVerb(MenuRoots[1], "%1", executable);
            InstallVerb(MenuRoots[2], "%1", executable);
            InstallVerb(MenuRoots[3], "%V", executable);
        }

        public static void Uninstall()
        {
            foreach (string path in MenuRoots)
                Registry.CurrentUser.DeleteSubKeyTree(path, false);
        }

        private static void InstallVerb(string keyPath, string targetToken, string executable)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath))
            {
                key.SetValue("MUIVerb", "使用 RoboCopy Paste 粘贴");
                key.SetValue("Icon", executable);
                key.SetValue("Position", "Top");
            }
            using (RegistryKey command = Registry.CurrentUser.CreateSubKey(keyPath + @"\command"))
            {
                command.SetValue(null, Quote(executable) + " --paste-target \"" + targetToken + "\"");
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
