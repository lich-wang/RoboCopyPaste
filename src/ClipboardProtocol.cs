using System;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RoboCopyPaste
{
    internal sealed class CopyMetadata
    {
        public string Token;
        public string InstanceId;
        public string Machine;
        public bool TerminalSession;
        public string[] Paths;
    }

    internal sealed class CopyRequest
    {
        public string Token;
        public string DestinationMachine;
        public string Destination;
    }

    internal static class ClipboardProtocol
    {
        public const string CopyFormat = "RoboCopyPaste.Copy.v1";
        public const string RequestFormat = "RoboCopyPaste.Request.v1";

        public static void SetCopy(CopyMetadata metadata)
        {
            var list = new StringCollection();
            list.AddRange(metadata.Paths);
            var data = new DataObject();
            data.SetFileDropList(list);
            data.SetData(CopyFormat, false, SerializeCopy(metadata));
            data.SetData("Preferred DropEffect", new MemoryStream(new byte[] { 1, 0, 0, 0 }));
            SetDataObjectWithRetry(data);
        }

        public static string[] TryGetFilePaths()
        {
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    if (!Clipboard.ContainsFileDropList()) return null;
                    StringCollection values = Clipboard.GetFileDropList();
                    var result = new string[values.Count];
                    values.CopyTo(result, 0);
                    return result;
                }
                catch (System.Runtime.InteropServices.ExternalException) { Thread.Sleep(25); }
            }
            return null;
        }

        public static CopyMetadata TryGetCopyMetadata()
        {
            string value = GetString(CopyFormat);
            return value == null ? null : DeserializeCopy(value);
        }

        public static CopyRequest TryGetRequest()
        {
            string value = GetString(RequestFormat);
            return value == null ? null : DeserializeRequest(value);
        }

        public static int TryGetDropEffect()
        {
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    IDataObject data = Clipboard.GetDataObject();
                    if (data == null || !data.GetDataPresent("Preferred DropEffect", false)) return 0;
                    object value = data.GetData("Preferred DropEffect", false);
                    var stream = value as Stream;
                    if (stream == null) return 0;
                    int effect = stream.ReadByte();
                    if (stream.CanSeek) stream.Position = 0;
                    return effect;
                }
                catch (System.Runtime.InteropServices.ExternalException) { Thread.Sleep(25); }
                catch { return 0; }
            }
            return 0;
        }

        public static void SetRequest(CopyRequest request)
        {
            var data = new DataObject();
            data.SetData(RequestFormat, false, SerializeRequest(request));
            SetDataObjectWithRetry(data);
        }

        private static string GetString(string format)
        {
            for (int i = 0; i < 4; i++)
            {
                try
                {
                    IDataObject data = Clipboard.GetDataObject();
                    if (data == null || !data.GetDataPresent(format, false)) return null;
                    object value = data.GetData(format, false);
                    return value as string;
                }
                catch (System.Runtime.InteropServices.ExternalException) { Thread.Sleep(25); }
            }
            return null;
        }

        private static void SetDataObjectWithRetry(IDataObject data)
        {
            Exception last = null;
            for (int i = 0; i < 6; i++)
            {
                try
                {
                    Clipboard.SetDataObject(data, true, 5, 50);
                    return;
                }
                catch (System.Runtime.InteropServices.ExternalException ex)
                {
                    last = ex;
                    Thread.Sleep(35);
                }
            }
            throw last ?? new InvalidOperationException("无法访问剪贴板。");
        }

        private static string SerializeCopy(CopyMetadata value)
        {
            var builder = new StringBuilder();
            builder.AppendLine("RCP1");
            builder.AppendLine(value.Token);
            builder.AppendLine(value.InstanceId);
            builder.AppendLine(B64(value.Machine));
            builder.AppendLine(value.TerminalSession ? "1" : "0");
            builder.AppendLine(value.Paths.Length.ToString());
            foreach (string path in value.Paths) builder.AppendLine(B64(path));
            return builder.ToString();
        }

        private static CopyMetadata DeserializeCopy(string value)
        {
            try
            {
                string[] lines = value.Replace("\r", string.Empty).Split('\n');
                if (lines.Length < 6 || lines[0] != "RCP1") return null;
                int count = int.Parse(lines[5]);
                if (count < 1 || count > 10000 || lines.Length < 6 + count) return null;
                var paths = new string[count];
                for (int i = 0; i < count; i++) paths[i] = UnB64(lines[6 + i]);
                return new CopyMetadata
                {
                    Token = lines[1], InstanceId = lines[2], Machine = UnB64(lines[3]),
                    TerminalSession = lines[4] == "1", Paths = paths
                };
            }
            catch { return null; }
        }

        private static string SerializeRequest(CopyRequest value)
        {
            return "RCPR1\n" + value.Token + "\n" + B64(value.DestinationMachine) + "\n" + B64(value.Destination) + "\n";
        }

        private static CopyRequest DeserializeRequest(string value)
        {
            try
            {
                string[] lines = value.Replace("\r", string.Empty).Split('\n');
                if (lines.Length < 4 || lines[0] != "RCPR1") return null;
                return new CopyRequest { Token = lines[1], DestinationMachine = UnB64(lines[2]), Destination = UnB64(lines[3]) };
            }
            catch { return null; }
        }

        private static string B64(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static string UnB64(string value)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }
    }
}
