using System;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace RoboCopyPaste
{
    internal sealed class AppSettings
    {
        public bool Enabled = true;
        public bool StartWithWindows = false;
        public bool ConfirmOverwrite = true;
        public int Threads = 16;
        public int Retries = 3;
        public int WaitSeconds = 2;

        public static readonly string DataDirectory = ResolveDataDirectory();
        private static readonly string SettingsPath = Path.Combine(DataDirectory, "settings.ini");

        private static string ResolveDataDirectory()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string portableRoot = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
            string[] candidates =
            {
                string.IsNullOrWhiteSpace(local) ? null : Path.Combine(local, "RoboCopyPaste"),
                string.IsNullOrWhiteSpace(portableRoot) ? null : Path.Combine(portableRoot, "data")
            };
            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                try
                {
                    Directory.CreateDirectory(candidate);
                    string probe = Path.Combine(candidate, ".write-" + Guid.NewGuid().ToString("N"));
                    File.WriteAllText(probe, "ok");
                    File.Delete(probe);
                    return candidate;
                }
                catch { }
            }
            return Path.Combine(portableRoot ?? ".", "data");
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            try
            {
                if (!File.Exists(SettingsPath)) return settings;
                foreach (string raw in File.ReadAllLines(SettingsPath, Encoding.UTF8))
                {
                    string[] pair = raw.Split(new[] { '=' }, 2);
                    if (pair.Length != 2) continue;
                    string key = pair[0].Trim();
                    string value = pair[1].Trim();
                    bool flag;
                    int number;
                    if (key == "Enabled" && bool.TryParse(value, out flag)) settings.Enabled = flag;
                    else if (key == "StartWithWindows" && bool.TryParse(value, out flag)) settings.StartWithWindows = flag;
                    else if (key == "ConfirmOverwrite" && bool.TryParse(value, out flag)) settings.ConfirmOverwrite = flag;
                    else if (key == "Threads" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) settings.Threads = Clamp(number, 1, 128);
                    else if (key == "Retries" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) settings.Retries = Clamp(number, 0, 100);
                    else if (key == "WaitSeconds" && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) settings.WaitSeconds = Clamp(number, 0, 300);
                }
            }
            catch { }
            return settings;
        }

        public void Save()
        {
            Directory.CreateDirectory(DataDirectory);
            var lines = new[]
            {
                "Enabled=" + Enabled,
                "StartWithWindows=" + StartWithWindows,
                "ConfirmOverwrite=" + ConfirmOverwrite,
                "Threads=" + Threads.ToString(CultureInfo.InvariantCulture),
                "Retries=" + Retries.ToString(CultureInfo.InvariantCulture),
                "WaitSeconds=" + WaitSeconds.ToString(CultureInfo.InvariantCulture)
            };
            File.WriteAllLines(SettingsPath, lines, new UTF8Encoding(false));
            ApplyStartup(StartWithWindows);
        }

        private static int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private static void ApplyStartup(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
            {
                if (enabled)
                    key.SetValue("RoboCopyPaste", "\"" + System.Windows.Forms.Application.ExecutablePath + "\" --startup");
                else
                    key.DeleteValue("RoboCopyPaste", false);
            }
        }
    }
}
