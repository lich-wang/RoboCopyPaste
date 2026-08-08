using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RoboCopyPaste
{
    internal sealed class CopyOperation
    {
        public string[] Sources;
        public string Destination;
        public string Description;
        public bool SkipExisting;
    }

    internal sealed class CopyResult
    {
        public bool Success;
        public bool Cancelled;
        public int ExitCode;
        public string Error;
    }

    internal sealed class RobocopyRunner
    {
        private readonly object gate = new object();
        private Process current;

        public void Cancel()
        {
            lock (gate)
            {
                if (current != null && !current.HasExited)
                {
                    try { current.Kill(); }
                    catch { }
                }
            }
        }

        public async Task<CopyResult> RunAsync(CopyOperation operation, AppSettings settings,
            Action<string> output, CancellationToken token)
        {
            var final = new CopyResult { Success = true, ExitCode = 0 };
            foreach (string source in operation.Sources)
            {
                if (token.IsCancellationRequested)
                    return new CopyResult { Success = false, Cancelled = true, ExitCode = -1 };

                string error;
                if (!Validate(source, operation.Destination, out error))
                    return new CopyResult { Success = false, Error = error, ExitCode = 16 };

                string arguments = BuildArguments(source, operation.Destination, operation.SkipExisting, settings);
                output("robocopy.exe " + arguments);
                int code;
                try { code = await RunProcessAsync(arguments, output, token); }
                catch (OperationCanceledException)
                {
                    return new CopyResult { Success = false, Cancelled = true, ExitCode = -1 };
                }
                catch (Exception ex)
                {
                    return new CopyResult { Success = false, Error = ex.Message, ExitCode = 16 };
                }

                final.ExitCode = Math.Max(final.ExitCode, code);
                if (code >= 8)
                {
                    final.Success = false;
                    final.Error = "Robocopy 返回错误代码 " + code.ToString(CultureInfo.InvariantCulture);
                    return final;
                }
            }
            return final;
        }

        private async Task<int> RunProcessAsync(string arguments, Action<string> output, CancellationToken token)
        {
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "robocopy.exe"),
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
                StandardErrorEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage)
            };
            var process = new Process { StartInfo = start, EnableRaisingEvents = true };
            var completion = new TaskCompletionSource<int>();
            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (!string.IsNullOrWhiteSpace(e.Data)) output(e.Data); };
            process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (!string.IsNullOrWhiteSpace(e.Data)) output("[stderr] " + e.Data); };
            process.Exited += delegate { try { completion.TrySetResult(process.ExitCode); } catch { } };

            lock (gate) current = process;
            try
            {
                if (!process.Start()) throw new InvalidOperationException("无法启动系统 Robocopy。");
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                using (token.Register(delegate { try { if (!process.HasExited) process.Kill(); } catch { } }))
                {
                    int code = await completion.Task;
                    if (token.IsCancellationRequested) throw new OperationCanceledException();
                    return code;
                }
            }
            finally
            {
                lock (gate) if (ReferenceEquals(current, process)) current = null;
                process.Dispose();
            }
        }

        private static string BuildArguments(string source, string destination, bool skipExisting, AppSettings settings)
        {
            bool directory = Directory.Exists(source);
            string sourceDirectory = directory ? source : Path.GetDirectoryName(source);
            string destinationDirectory = directory ? Path.Combine(destination, new DirectoryInfo(source).Name) : destination;
            var args = new List<string>();
            args.Add(Quote(sourceDirectory));
            args.Add(Quote(destinationDirectory));
            if (!directory) args.Add(Quote(Path.GetFileName(source)));
            if (directory) args.Add("/E");
            args.Add("/Z");
            args.Add("/COPY:DAT");
            args.Add("/DCOPY:DAT");
            args.Add("/XJ");
            args.Add("/MT:" + settings.Threads.ToString(CultureInfo.InvariantCulture));
            args.Add("/R:" + settings.Retries.ToString(CultureInfo.InvariantCulture));
            args.Add("/W:" + settings.WaitSeconds.ToString(CultureInfo.InvariantCulture));
            args.Add("/TEE");
            if (skipExisting)
            {
                args.Add("/XC");
                args.Add("/XN");
                args.Add("/XO");
            }
            return string.Join(" ", args.ToArray());
        }

        private static bool Validate(string source, string destination, out string error)
        {
            error = null;
            if (!File.Exists(source) && !Directory.Exists(source))
            {
                error = "源不存在或当前不可访问：" + source;
                return false;
            }
            if (!Directory.Exists(destination))
            {
                error = "目标目录不存在：" + destination;
                return false;
            }
            if (Directory.Exists(source))
            {
                string sourceFull = AppendSeparator(Path.GetFullPath(source));
                string target = AppendSeparator(Path.GetFullPath(Path.Combine(destination, new DirectoryInfo(source).Name)));
                if (target.StartsWith(sourceFull, StringComparison.OrdinalIgnoreCase))
                {
                    error = "不能把文件夹复制到其自身内部：" + source;
                    return false;
                }
            }
            return true;
        }

        private static string AppendSeparator(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
