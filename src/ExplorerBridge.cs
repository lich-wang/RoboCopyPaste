using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace RoboCopyPaste
{
    internal static class ExplorerBridge
    {
        public static string GetDestination(IntPtr foreground)
        {
            string cls = NativeMethods.ClassName(foreground);
            if (cls == "Progman" || cls == "WorkerW")
                return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            object shell = null;
            try
            {
                Type type = Type.GetTypeFromProgID("Shell.Application");
                shell = Activator.CreateInstance(type);
                dynamic windows = ((dynamic)shell).Windows();
                int count = windows.Count;
                for (int i = 0; i < count; i++)
                {
                    dynamic window = windows.Item(i);
                    try
                    {
                        if (new IntPtr(Convert.ToInt64(window.HWND)) != foreground) continue;
                        string path = null;
                        try { path = Convert.ToString(window.Document.Folder.Self.Path); }
                        catch { }
                        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path))
                        {
                            string locationUrl = Convert.ToString(window.LocationURL);
                            Uri uri;
                            if (Uri.TryCreate(locationUrl, UriKind.Absolute, out uri) && uri.IsFile)
                                path = uri.LocalPath;
                        }
                        if (!string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path))
                            return Path.GetFullPath(path);
                    }
                    catch { }
                    finally { Release(window); }
                }
            }
            catch { }
            finally { Release(shell); }
            return null;
        }

        public static string[] GetSelectedPaths(IntPtr foreground)
        {
            var paths = new List<string>();
            object shell = null;
            try
            {
                Type type = Type.GetTypeFromProgID("Shell.Application");
                shell = Activator.CreateInstance(type);
                dynamic windows = ((dynamic)shell).Windows();
                int count = windows.Count;
                for (int i = 0; i < count; i++)
                {
                    dynamic window = windows.Item(i);
                    try
                    {
                        if (new IntPtr(Convert.ToInt64(window.HWND)) != foreground) continue;
                        dynamic selected = window.Document.SelectedItems();
                        int selectedCount = selected.Count;
                        for (int j = 0; j < selectedCount; j++)
                        {
                            dynamic item = selected.Item(j);
                            try
                            {
                                string path = Convert.ToString(item.Path);
                                if (!string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)))
                                    paths.Add(Path.GetFullPath(path));
                            }
                            finally { Release(item); }
                        }
                        Release(selected);
                        break;
                    }
                    catch { }
                    finally { Release(window); }
                }
            }
            catch { }
            finally { Release(shell); }
            return paths.ToArray();
        }

        private static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                try { Marshal.FinalReleaseComObject(value); }
                catch { }
            }
        }
    }
}
