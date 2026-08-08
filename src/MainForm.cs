using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RoboCopyPaste
{
    internal sealed class MainForm : Form
    {
        private readonly AppSettings settings;
        private readonly RobocopyRunner runner = new RobocopyRunner();
        private readonly Queue<CopyOperation> queue = new Queue<CopyOperation>();
        private readonly Dictionary<string, CopyMetadata> outgoing = new Dictionary<string, CopyMetadata>();
        private readonly HashSet<string> processedRequests = new HashSet<string>();
        private readonly string instanceId = Guid.NewGuid().ToString("N");
        private readonly string logDirectory = Path.Combine(AppSettings.DataDirectory, "logs");

        private CheckBox enabledBox;
        private CheckBox startupBox;
        private CheckBox confirmBox;
        private CheckBox contextMenuBox;
        private NumericUpDown threadsBox;
        private NumericUpDown retriesBox;
        private NumericUpDown waitBox;
        private Label statusLabel;
        private Label routeLabel;
        private TextBox outputBox;
        private ProgressBar progress;
        private Button cancelButton;
        private NotifyIcon tray;
        private System.Windows.Forms.Timer clipboardTimer;
        private CancellationTokenSource cancellation;
        private bool running;
        private bool allowExit;
        private volatile bool fileClipboardReady;
        private volatile bool replayingNativePaste;
        private bool hotkeyRegistered;
        private readonly string initialPasteDestination;
        private readonly bool contextInvocation;

        public MainForm(bool startHidden, string pasteDestination, bool invokedFromContextMenu)
        {
            initialPasteDestination = pasteDestination;
            contextInvocation = invokedFromContextMenu;
            allowExit = contextInvocation;
            settings = AppSettings.Load();
            BuildUi();
            Shown += delegate
            {
                bool hookOk = contextInvocation || !settings.Enabled || StartHotkey();
                if (!contextInvocation)
                {
                    NativeMethods.AddClipboardFormatListener(Handle);
                    clipboardTimer = new System.Windows.Forms.Timer();
                    clipboardTimer.Interval = 500;
                    clipboardTimer.Tick += delegate { HandleClipboardUpdate(); };
                    clipboardTimer.Start();
                }
                if (!contextInvocation)
                {
                    try { ContextMenuIntegration.Apply(settings.ContextMenuEnabled); }
                    catch (Exception ex)
                    {
                        contextMenuBox.Checked = false;
                        WriteLog("资源管理器右键菜单注册失败：" + ex.Message);
                    }
                }
                if (!hookOk)
                {
                    settings.Enabled = false;
                    enabledBox.Checked = false;
                    SetStatus("无法注册 Ctrl+V 热键，快捷键接管已关闭。", Color.Firebrick);
                }
                else
                {
                    SetStatus(settings.Enabled ? "已接管资源管理器中的文件 Ctrl+V" : "接管已暂停", settings.Enabled ? Color.SeaGreen : Color.DimGray);
                }
                if (startHidden) Hide();
                if (!string.IsNullOrWhiteSpace(initialPasteDestination))
                {
                    BeginInvoke((Action)delegate
                    {
                        if (!HandlePasteToDestination(initialPasteDestination, "资源管理器右键菜单"))
                        {
                            SetStatus("剪贴板中没有可复制的文件", Color.DarkOrange);
                            AppendOutput("右键粘贴未执行：剪贴板中没有有效的文件或文件夹。");
                        }
                    });
                }
            };
        }

        private void BuildUi()
        {
            Text = "RoboCopy Paste";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(720, 520);
            Size = new Size(850, 620);
            Font = new Font("Microsoft YaHei UI", 9F);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 1, RowCount = 5 };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var title = new Label { AutoSize = true, Text = "RoboCopy Paste", Font = new Font(Font.FontFamily, 18F, FontStyle.Bold), ForeColor = Color.FromArgb(35, 49, 73), Margin = new Padding(0, 0, 0, 4) };
            root.Controls.Add(title);

            var intro = new Label { AutoSize = true, MaximumSize = new Size(790, 0), Text = "在 Windows 资源管理器中复制文件后按 Ctrl+V，或在目标文件夹右键选择“使用 RoboCopy Paste 粘贴”。任务由系统自带 robocopy.exe 执行；文字粘贴和其他应用保持原样。", ForeColor = Color.DimGray, Margin = new Padding(0, 0, 0, 12) };
            root.Controls.Add(intro);

            var settingsPanel = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 6, Padding = new Padding(10), BackColor = Color.FromArgb(245, 247, 250), Margin = new Padding(0, 0, 0, 12) };
            enabledBox = new CheckBox { AutoSize = true, Text = "启用接管", Checked = settings.Enabled, Margin = new Padding(3, 6, 18, 3) };
            startupBox = new CheckBox { AutoSize = true, Text = "开机启动", Checked = settings.StartWithWindows, Margin = new Padding(3, 6, 18, 3) };
            confirmBox = new CheckBox { AutoSize = true, Text = "覆盖前确认", Checked = settings.ConfirmOverwrite, Margin = new Padding(3, 6, 18, 3) };
            contextMenuBox = new CheckBox { AutoSize = true, Text = "资源管理器右键菜单", Checked = settings.ContextMenuEnabled, Margin = new Padding(3, 6, 18, 3) };
            threadsBox = MakeNumber(settings.Threads, 1, 128);
            retriesBox = MakeNumber(settings.Retries, 0, 100);
            waitBox = MakeNumber(settings.WaitSeconds, 0, 300);
            settingsPanel.Controls.Add(enabledBox, 0, 0);
            settingsPanel.Controls.Add(startupBox, 1, 0);
            settingsPanel.Controls.Add(confirmBox, 2, 0);
            settingsPanel.Controls.Add(contextMenuBox, 3, 0);
            settingsPanel.Controls.Add(new Label { AutoSize = true, Text = "线程", Margin = new Padding(3, 8, 3, 3) }, 0, 1);
            settingsPanel.Controls.Add(threadsBox, 1, 1);
            settingsPanel.Controls.Add(new Label { AutoSize = true, Text = "重试", Margin = new Padding(12, 8, 3, 3) }, 2, 1);
            settingsPanel.Controls.Add(retriesBox, 3, 1);
            settingsPanel.Controls.Add(new Label { AutoSize = true, Text = "等待(秒)", Margin = new Padding(12, 8, 3, 3) }, 4, 1);
            settingsPanel.Controls.Add(waitBox, 5, 1);
            root.Controls.Add(settingsPanel);

            var workPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            statusLabel = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Text = "正在初始化…", Margin = new Padding(0, 0, 0, 4) };
            routeLabel = new Label { AutoSize = true, ForeColor = Color.DimGray, Text = "等待复制任务", Margin = new Padding(0, 0, 0, 8) };
            progress = new ProgressBar { Dock = DockStyle.Top, Height = 12, Style = ProgressBarStyle.Blocks, Margin = new Padding(0, 0, 0, 8) };
            outputBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, BackColor = Color.FromArgb(24, 27, 33), ForeColor = Color.Gainsboro, Font = new Font("Consolas", 9F) };
            workPanel.Controls.Add(statusLabel, 0, 0);
            workPanel.Controls.Add(routeLabel, 0, 1);
            workPanel.Controls.Add(progress, 0, 2);
            workPanel.Controls.Add(outputBox, 0, 3);
            workPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            workPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            workPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            workPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(workPanel);

            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0, 10, 0, 0) };
            var hideButton = new Button { AutoSize = true, Text = "隐藏到托盘", Padding = new Padding(8, 3, 8, 3) };
            var saveButton = new Button { AutoSize = true, Text = "保存设置", Padding = new Padding(8, 3, 8, 3) };
            var logButton = new Button { AutoSize = true, Text = "打开日志目录", Padding = new Padding(8, 3, 8, 3) };
            var exitButton = new Button { AutoSize = true, Text = "退出", Padding = new Padding(8, 3, 8, 3) };
            cancelButton = new Button { AutoSize = true, Text = "取消当前任务", Enabled = false, Padding = new Padding(8, 3, 8, 3) };
            hideButton.Click += delegate { Hide(); };
            saveButton.Click += delegate { SaveSettings(); };
            logButton.Click += delegate { Directory.CreateDirectory(logDirectory); Process.Start("explorer.exe", "\"" + logDirectory + "\""); };
            exitButton.Click += delegate { allowExit = true; Close(); };
            cancelButton.Click += delegate { if (cancellation != null) cancellation.Cancel(); runner.Cancel(); };
            buttons.Controls.Add(hideButton);
            buttons.Controls.Add(saveButton);
            buttons.Controls.Add(logButton);
            buttons.Controls.Add(exitButton);
            buttons.Controls.Add(cancelButton);
            root.Controls.Add(buttons);

            var menu = new ContextMenuStrip();
            menu.Items.Add("打开", null, delegate { ShowWindow(); });
            menu.Items.Add("启用/暂停接管", null, delegate { enabledBox.Checked = !enabledBox.Checked; SaveSettings(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { allowExit = true; Close(); });
            tray = new NotifyIcon { Icon = Icon, Text = "RoboCopy Paste", Visible = true, ContextMenuStrip = menu };
            tray.DoubleClick += delegate { ShowWindow(); };
            FormClosing += OnFormClosing;
        }

        private static NumericUpDown MakeNumber(int value, int minimum, int maximum)
        {
            return new NumericUpDown { Width = 65, Minimum = minimum, Maximum = maximum, Value = Math.Max(minimum, Math.Min(maximum, value)) };
        }

        private void HandleHotkey()
        {
            if (replayingNativePaste) return;
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            WriteLog("HOTKEY foreground=" + foreground + " class=" + NativeMethods.ClassName(foreground) + " fileClipboard=" + fileClipboardReady);
            if (settings.Enabled && fileClipboardReady && NativeMethods.IsExplorerOrDesktop(foreground))
            {
                ProcessInterceptedPaste(foreground);
                return;
            }
            ReplayNativePaste(foreground);
        }

        private async void ProcessInterceptedPaste(IntPtr foreground)
        {
            bool handled = HandlePaste(foreground);
            if (handled) return;

            AppendOutput("检测到文件粘贴，但当前资源管理器目标无法解析；已回退 Windows。");
            await Task.Delay(80);
            if (NativeMethods.GetForegroundWindow() != foreground) return;
            ReplayNativePaste(foreground);
        }

        private async void ReplayNativePaste(IntPtr foreground)
        {
            if (NativeMethods.GetForegroundWindow() != foreground) return;
            replayingNativePaste = true;
            StopHotkey();
            await Task.Delay(60);
            if (NativeMethods.GetForegroundWindow() == foreground)
                NativeMethods.SendCtrlV();
            await Task.Delay(100);
            replayingNativePaste = false;
            if (settings.Enabled) StartHotkey();
        }

        private bool StartHotkey()
        {
            if (hotkeyRegistered) return true;
            hotkeyRegistered = NativeMethods.RegisterHotKey(Handle, NativeMethods.HOTKEY_ID,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_NOREPEAT, NativeMethods.VK_V);
            return hotkeyRegistered;
        }

        private void StopHotkey()
        {
            if (!hotkeyRegistered) return;
            NativeMethods.UnregisterHotKey(Handle, NativeMethods.HOTKEY_ID);
            hotkeyRegistered = false;
        }

        private void EnrichFileClipboard()
        {
            CopyMetadata existingMetadata = ClipboardProtocol.TryGetCopyMetadata();
            if (existingMetadata != null)
            {
                fileClipboardReady = existingMetadata.Paths != null && existingMetadata.Paths.Length > 0;
                return;
            }
            if (ClipboardProtocol.TryGetDropEffect() == 2)
            {
                fileClipboardReady = false;
                return;
            }
            string[] paths = ExistingPaths(ClipboardProtocol.TryGetFilePaths());
            fileClipboardReady = paths != null && paths.Length > 0;
            if (!fileClipboardReady) return;
            var metadata = new CopyMetadata
            {
                Token = Guid.NewGuid().ToString("N"),
                InstanceId = instanceId,
                Machine = Environment.MachineName,
                TerminalSession = SystemInformation.TerminalServerSession,
                Paths = paths
            };
            try
            {
                ClipboardProtocol.SetCopy(metadata);
                outgoing.Clear();
                outgoing[metadata.Token] = metadata;
                SetStatus("已记录 " + paths.Length + " 个项目，等待粘贴", Color.SeaGreen);
                routeLabel.Text = Shorten(paths[0]);
            }
            catch (Exception ex)
            {
                WriteLog("RDP 元数据附加失败（不影响本机粘贴）：" + ex.Message);
            }
        }

        private bool HandlePaste(IntPtr foreground)
        {
            string destination = ExplorerBridge.GetDestination(foreground);
            if (string.IsNullOrEmpty(destination)) return false;
            return HandlePasteToDestination(destination, "Ctrl+V");
        }

        private bool HandlePasteToDestination(string destination, string description)
        {
            if (string.IsNullOrWhiteSpace(destination) || !Directory.Exists(destination))
            {
                AppendOutput("粘贴目标不可访问：" + (destination ?? "(空)"));
                return false;
            }

            CopyMetadata metadata = ClipboardProtocol.TryGetCopyMetadata();
            string[] paths = ClipboardProtocol.TryGetFilePaths();
            if (metadata != null && metadata.Paths != null && metadata.Paths.Length > 0)
            {
                string[] resolved = ResolveSources(metadata);
                if (resolved != null)
                {
                    BeginInvoke((Action)delegate { PrepareAndQueue(resolved, destination, description); });
                    return true;
                }

                if (!SystemInformation.TerminalServerSession && metadata.TerminalSession)
                {
                    try
                    {
                        ClipboardProtocol.SetRequest(new CopyRequest
                        {
                            Token = metadata.Token,
                            DestinationMachine = Environment.MachineName,
                            Destination = destination
                        });
                        SetStatus("已请求远程端通过重定向磁盘复制", Color.DarkOrange);
                        routeLabel.Text = metadata.Machine + " → " + destination;
                        tray.ShowBalloonTip(3000, "RoboCopy Paste", "复制请求已发送到远程会话；请在远程端查看进度。", ToolTipIcon.Info);
                        return true;
                    }
                    catch { return false; }
                }
            }

            string[] local = ExistingPaths(paths);
            if (local != null)
            {
                BeginInvoke((Action)delegate { PrepareAndQueue(local, destination, description); });
                return true;
            }
            return false;
        }

        private string[] ResolveSources(CopyMetadata metadata)
        {
            string[] direct = ExistingPaths(metadata.Paths);
            if (direct != null && (metadata.InstanceId == instanceId || metadata.Machine == Environment.MachineName)) return direct;

            if (SystemInformation.TerminalServerSession && !metadata.TerminalSession)
            {
                var mapped = new List<string>();
                foreach (string path in metadata.Paths)
                {
                    string value = MapClientPath(path);
                    if (value == null || (!File.Exists(value) && !Directory.Exists(value))) return null;
                    mapped.Add(value);
                }
                return mapped.ToArray();
            }
            return direct;
        }

        private static string[] ExistingPaths(string[] paths)
        {
            if (paths == null || paths.Length == 0) return null;
            foreach (string path in paths)
                if (!File.Exists(path) && !Directory.Exists(path)) return null;
            return paths;
        }

        private static string MapClientPath(string path)
        {
            try
            {
                string root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root) || root.Length < 2 || root[1] != ':') return null;
                string drive = char.ToUpperInvariant(root[0]).ToString();
                string rest = path.Substring(root.Length).TrimStart('\\', '/');
                return "\\\\tsclient\\" + drive + "\\" + rest;
            }
            catch { return null; }
        }

        private void PrepareAndQueue(string[] sources, string destination, string description)
        {
            bool collision = sources.Any(delegate(string source)
            {
                string target = Path.Combine(destination, Directory.Exists(source) ? new DirectoryInfo(source).Name : Path.GetFileName(source));
                return File.Exists(target) || Directory.Exists(target);
            });
            bool skipExisting = false;
            if (collision && settings.ConfirmOverwrite)
            {
                DialogResult answer = MessageBox.Show(this,
                    "目标中存在同名项目。\n\n是：让 Robocopy 更新不同的文件\n否：跳过所有已存在文件\n取消：不执行",
                    "确认覆盖", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (answer == DialogResult.Cancel) return;
                skipExisting = answer == DialogResult.No;
            }
            queue.Enqueue(new CopyOperation { Sources = sources, Destination = destination, Description = description, SkipExisting = skipExisting });
            AppendOutput("已加入队列：" + sources.Length + " 个项目 → " + destination);
            if (!running) StartNext();
        }

        private async void StartNext()
        {
            if (running || queue.Count == 0) return;
            running = true;
            CopyOperation operation = queue.Dequeue();
            cancellation = new CancellationTokenSource();
            cancelButton.Enabled = true;
            progress.Style = ProgressBarStyle.Marquee;
            routeLabel.Text = operation.Sources[0] + " → " + operation.Destination;
            SetStatus("正在使用系统 Robocopy 复制…", Color.RoyalBlue);
            WriteLog("=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + routeLabel.Text + " ===");

            CopyResult result = await runner.RunAsync(operation, settings, AppendOutput, cancellation.Token);
            progress.Style = ProgressBarStyle.Blocks;
            progress.Value = result.Success ? 100 : 0;
            cancelButton.Enabled = false;
            cancellation.Dispose();
            cancellation = null;
            running = false;

            if (result.Cancelled)
            {
                SetStatus("任务已取消", Color.DarkOrange);
                AppendOutput("任务已取消。\r\n");
            }
            else if (result.Success)
            {
                SetStatus("复制完成（Robocopy 代码 " + result.ExitCode + "）", Color.SeaGreen);
                AppendOutput("复制完成。\r\n");
                tray.ShowBalloonTip(2500, "RoboCopy Paste", "复制完成", ToolTipIcon.Info);
            }
            else
            {
                SetStatus("复制失败", Color.Firebrick);
                AppendOutput(result.Error ?? "未知错误");
                tray.ShowBalloonTip(3500, "RoboCopy Paste", result.Error ?? "复制失败", ToolTipIcon.Error);
            }
            if (queue.Count > 0) StartNext();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == NativeMethods.WM_HOTKEY && message.WParam.ToInt32() == NativeMethods.HOTKEY_ID)
            {
                HandleHotkey();
                return;
            }
            base.WndProc(ref message);
            if (message.Msg == NativeMethods.WM_CLIPBOARDUPDATE) HandleClipboardUpdate();
        }

        private void HandleClipboardUpdate()
        {
            if (!settings.Enabled) return;
            CopyRequest request = ClipboardProtocol.TryGetRequest();
            if (request != null) fileClipboardReady = false;
            if (request != null && SystemInformation.TerminalServerSession && !processedRequests.Contains(request.Token))
            {
                CopyMetadata metadata;
                if (!outgoing.TryGetValue(request.Token, out metadata)) return;
                string mappedDestination = MapClientPath(request.Destination);
                if (mappedDestination == null || !Directory.Exists(mappedDestination))
                {
                    AppendOutput("收到反向复制请求，但客户端驱动器未重定向或目标不可访问：" + request.Destination);
                    return;
                }
                processedRequests.Add(request.Token);
                PrepareAndQueue(metadata.Paths, mappedDestination, "RDP 反向请求");
                return;
            }
            if (request == null) EnrichFileClipboard();
        }

        private void SaveSettings()
        {
            settings.Enabled = enabledBox.Checked;
            settings.StartWithWindows = startupBox.Checked;
            settings.ConfirmOverwrite = confirmBox.Checked;
            settings.ContextMenuEnabled = contextMenuBox.Checked;
            settings.Threads = (int)threadsBox.Value;
            settings.Retries = (int)retriesBox.Value;
            settings.WaitSeconds = (int)waitBox.Value;
            try
            {
                settings.Save();
                if (settings.Enabled && !contextInvocation)
                {
                    if (!StartHotkey())
                        throw new InvalidOperationException("Ctrl+V 已被其他程序注册，无法启用接管。");
                }
                else StopHotkey();
                SetStatus(settings.Enabled ? "设置已保存，接管已启用" : "设置已保存，接管已暂停", settings.Enabled ? Color.SeaGreen : Color.DimGray);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存设置失败：" + ex.Message, "RoboCopy Paste", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AppendOutput(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action<string>)AppendOutput, text);
                return;
            }
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text;
            outputBox.AppendText(line + Environment.NewLine);
            WriteLog(line);
        }

        private void WriteLog(string text)
        {
            try
            {
                Directory.CreateDirectory(logDirectory);
                File.AppendAllText(Path.Combine(logDirectory, DateTime.Now.ToString("yyyy-MM-dd") + ".log"), text + Environment.NewLine);
            }
            catch { }
        }

        private void SetStatus(string text, Color color)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action<string, Color>)SetStatus, text, color);
                return;
            }
            statusLabel.Text = text;
            statusLabel.ForeColor = color;
            if (tray != null) tray.Text = text.Length > 60 ? text.Substring(0, 60) : text;
        }

        private void ShowWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!allowExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                tray.ShowBalloonTip(1800, "RoboCopy Paste", "程序仍在托盘运行。", ToolTipIcon.Info);
                return;
            }
            if (running)
            {
                DialogResult answer = MessageBox.Show(this, "复制任务仍在运行，确定要终止并退出吗？", "RoboCopy Paste",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    e.Cancel = true;
                    allowExit = false;
                    return;
                }
                if (cancellation != null) cancellation.Cancel();
                runner.Cancel();
            }
            if (!contextInvocation) NativeMethods.RemoveClipboardFormatListener(Handle);
            if (clipboardTimer != null)
            {
                clipboardTimer.Stop();
                clipboardTimer.Dispose();
            }
            StopHotkey();
            tray.Visible = false;
            tray.Dispose();
        }

        private static string Shorten(string value)
        {
            return value.Length <= 100 ? value : value.Substring(0, 47) + "…" + value.Substring(value.Length - 47);
        }
    }
}
