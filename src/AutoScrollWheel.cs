using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("自动滚轮")]
[assembly: System.Reflection.AssemblyDescription("按一个键开始或停止自动滚动鼠标滚轮")]
[assembly: System.Reflection.AssemblyVersion("1.2.0.0")]

namespace AutoScrollWheel
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool first;
            using (Mutex instance = new Mutex(true, "Local\\AutoScrollWheel_8A64BB31", out first))
            {
                if (!first)
                {
                    IntPtr receiver = Native.FindWindowEx(new IntPtr(-3), IntPtr.Zero, null, HotkeyReceiver.WindowName);
                    if (receiver != IntPtr.Zero) Native.PostMessage(receiver, HotkeyReceiver.RestoreMessage, IntPtr.Zero, IntPtr.Zero);
                    else MessageBox.Show("自动滚轮已经在运行。请查看任务栏或右下角托盘图标。", "自动滚轮", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new ScrollForm());
            }
        }
    }

    internal sealed class Preferences
    {
        internal string Hotkey = "F8";
        internal int Speed = 4;
        internal bool Up;
        internal bool Background = true;
        private static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoScrollWheel", "settings.ini"); }
        }

        internal static Preferences Load()
        {
            Preferences p = new Preferences();
            try
            {
                if (!File.Exists(FilePath)) return p;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] pair = line.Split(new char[] { '=' }, 2);
                    if (pair.Length != 2) continue;
                    int speed;
                    if (pair[0] == "Hotkey") p.Hotkey = pair[1];
                    if (pair[0] == "Speed" && int.TryParse(pair[1], out speed)) p.Speed = Math.Max(1, Math.Min(20, speed));
                    if (pair[0] == "Up") p.Up = pair[1] == "1";
                    if (pair[0] == "Background") p.Background = pair[1] == "1";
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return p;
        }

        internal void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllLines(FilePath, new string[] { "Hotkey=" + Hotkey, "Speed=" + Speed.ToString(CultureInfo.InvariantCulture), "Up=" + (Up ? "1" : "0"), "Background=" + (Background ? "1" : "0") });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal sealed class ScrollForm : Form
    {
        private static readonly string[] KeyNames = { "F6", "F7", "F8", "F9", "F10", "F11", "Pause", "ScrollLock" };
        private static readonly Keys[] KeyCodes = { Keys.F6, Keys.F7, Keys.F8, Keys.F9, Keys.F10, Keys.F11, Keys.Pause, Keys.Scroll };
        private readonly Preferences preferences = Preferences.Load();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly Stopwatch clock = new Stopwatch();
        private readonly Color ink = Color.FromArgb(29, 36, 41);
        private readonly Color green = Color.FromArgb(16, 116, 89);
        private readonly Color red = Color.FromArgb(185, 49, 59);
        private readonly Label status = new Label();
        private readonly ComboBox shortcut = new ComboBox();
        private readonly ComboBox scrollMode = new ComboBox();
        private readonly Label targetLabel = new Label();
        private readonly Button pick = new Button();
        private readonly NumericUpDown speed = new NumericUpDown();
        private readonly RadioButton down = new RadioButton();
        private readonly RadioButton up = new RadioButton();
        private readonly Button toggle = new Button();
        private readonly Button hide = new Button();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly ToolStripMenuItem trayStop = new ToolStripMenuItem("停止滚动");
        private readonly ToolTip tips = new ToolTip();
        private readonly int processId = Process.GetCurrentProcess().Id;
        private readonly HotkeyReceiver hotkeys = new HotkeyReceiver();
        private bool exitRequested;
        private bool running;
        private bool loading = true;
        private int registeredId;
        private int registeredIndex = -1;
        private bool escapeRegistered;
        private long lastWheel;
        private FixedScrollTarget target;
        private bool selecting;

        internal ScrollForm()
        {
            Text = "自动滚轮";
            Font = new Font("Microsoft YaHei UI", 9F);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(440, 484);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            ForeColor = ink;
            Icon = SystemIcons.Application;

            Label heading = LabelAt("自动滚轮", 28, 24, 380, 34);
            heading.Font = new Font(Font.FontFamily, 17F, FontStyle.Bold);
            status.SetBounds(28, 72, 384, 35);
            status.BackColor = Color.FromArgb(242, 245, 246);
            status.Padding = new Padding(12, 0, 6, 0);
            status.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(status);

            LabelAt("滚动模式", 28, 129, 118, 28);
            scrollMode.SetBounds(152, 126, 260, 30);
            scrollMode.DropDownStyle = ComboBoxStyle.DropDownList;
            scrollMode.Items.AddRange(new string[] { "固定区域（后台）", "跟随鼠标" });
            scrollMode.SelectedIndex = preferences.Background ? 0 : 1;
            scrollMode.AccessibleName = "滚动模式";
            Controls.Add(scrollMode);

            LabelAt("启停按键", 28, 177, 118, 28);
            shortcut.SetBounds(152, 174, 260, 30);
            shortcut.DropDownStyle = ComboBoxStyle.DropDownList;
            shortcut.Items.AddRange(KeyNames);
            shortcut.AccessibleName = "启停按键";
            int selected = Array.IndexOf(KeyNames, preferences.Hotkey);
            shortcut.SelectedIndex = selected < 0 ? 2 : selected;
            Controls.Add(shortcut);

            LabelAt("滚动速度", 28, 225, 118, 28);
            speed.SetBounds(152, 222, 168, 30);
            speed.Minimum = 1;
            speed.Maximum = 20;
            speed.Value = preferences.Speed;
            speed.AccessibleName = "滚动速度";
            Controls.Add(speed);
            LabelAt("格 / 秒", 336, 225, 76, 28);

            LabelAt("滚动方向", 28, 273, 118, 28);
            down.SetBounds(152, 271, 124, 30);
            down.Text = "↓  向下";
            down.AccessibleName = "向下滚动";
            up.SetBounds(288, 271, 124, 30);
            up.Text = "↑  向上";
            up.AccessibleName = "向上滚动";
            down.Checked = !preferences.Up;
            up.Checked = preferences.Up;
            Controls.Add(down);
            Controls.Add(up);

            LabelAt("滚动目标", 28, 319, 118, 28);
            targetLabel.SetBounds(152, 317, 260, 32);
            targetLabel.TextAlign = ContentAlignment.MiddleLeft;
            targetLabel.AutoEllipsis = true;
            targetLabel.AccessibleName = "滚动目标";
            Controls.Add(targetLabel);
            pick.SetBounds(152, 356, 260, 34);
            pick.FlatStyle = FlatStyle.Flat;
            pick.FlatAppearance.BorderColor = Color.FromArgb(213, 219, 222);
            pick.BackColor = Color.FromArgb(249, 250, 251);
            pick.Cursor = Cursors.Hand;
            pick.Click += delegate { BeginSelect(); };
            Controls.Add(pick);

            toggle.SetBounds(28, 412, 244, 44);
            toggle.FlatStyle = FlatStyle.Flat;
            toggle.FlatAppearance.BorderSize = 0;
            toggle.ForeColor = Color.White;
            toggle.Font = new Font(Font.FontFamily, 10F, FontStyle.Bold);
            toggle.Cursor = Cursors.Hand;
            toggle.Click += delegate
            {
                if (!running && preferences.Background && target == null) BeginSelect();
                else ToggleScroll();
            };
            Controls.Add(toggle);
            hide.SetBounds(284, 412, 128, 44);
            hide.Text = "收起到托盘";
            hide.FlatStyle = FlatStyle.Flat;
            hide.FlatAppearance.BorderColor = Color.FromArgb(213, 219, 222);
            hide.BackColor = Color.FromArgb(249, 250, 251);
            hide.Cursor = Cursors.Hand;
            hide.Click += delegate { HideToTray(); };
            Controls.Add(hide);
            tips.SetToolTip(toggle, "开始或停止滚动；Esc 随时停止");
            tips.SetToolTip(hide, "双击托盘图标可重新打开窗口");
            tips.SetToolTip(speed, "每格相当于鼠标滚轮转动一格，实际距离取决于目标软件");
            tips.SetToolTip(pick, "收起窗口后，将鼠标移到聊天记录上，再按启停键锁定并开始滚动");
            tips.SetToolTip(scrollMode, "固定区域会向选定窗口发送滚动消息；跟随鼠标会模拟普通滚轮");

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("打开窗口", null, delegate { RestoreWindow(); });
            trayStop.Enabled = false;
            trayStop.Click += delegate { StopScroll(null); };
            menu.Items.Add(trayStop);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { exitRequested = true; Close(); });
            tray.ContextMenuStrip = menu;
            tray.Icon = Icon;
            tray.Visible = true;
            tray.DoubleClick += delegate { RestoreWindow(); };
            hotkeys.Pressed += OnHotkey;
            hotkeys.RestoreRequested += RestoreWindow;

            shortcut.SelectedIndexChanged += delegate { if (!loading) ChangeShortcut(); };
            scrollMode.SelectedIndexChanged += delegate
            {
                if (loading) return;
                StopScroll(null);
                preferences.Background = scrollMode.SelectedIndex == 0;
                preferences.Save();
                UpdateAppearance();
            };
            speed.ValueChanged += delegate
            {
                if (loading) return;
                preferences.Speed = (int)speed.Value;
                preferences.Save();
                lastWheel = clock.ElapsedMilliseconds;
                UpdateAppearance();
            };
            up.CheckedChanged += delegate
            {
                if (loading) return;
                preferences.Up = up.Checked;
                preferences.Save();
                UpdateAppearance();
            };
            timer.Interval = 15;
            timer.Tick += OnTick;
            clock.Start();
            loading = false;
            UpdateAppearance();
            Shown += delegate { ChangeShortcut(); };
            Resize += delegate { if (WindowState == FormWindowState.Minimized) HideToTray(); };
            SystemEventsBridge.Attach(this);
        }

        private Label LabelAt(string text, int x, int y, int width, int height)
        {
            Label label = new Label();
            label.Text = text;
            label.SetBounds(x, y, width, height);
            label.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(label);
            return label;
        }

        private void ChangeShortcut()
        {
            StopScroll(null);
            int next = shortcut.SelectedIndex;
            if (next < 0 || next == registeredIndex) return;
            int newId = registeredId == 1 ? 2 : 1;
            if (!Native.RegisterHotKey(hotkeys.Handle, newId, 0x4000, (uint)KeyCodes[next]))
            {
                string message = KeyNames[next] + " 已被其他程序占用，请换一个按键。";
                if (registeredIndex >= 0)
                {
                    loading = true;
                    shortcut.SelectedIndex = registeredIndex;
                    loading = false;
                }
                status.Text = message;
                status.ForeColor = red;
                tips.SetToolTip(status, message);
                return;
            }
            if (registeredId != 0) Native.UnregisterHotKey(hotkeys.Handle, registeredId);
            registeredId = newId;
            registeredIndex = next;
            preferences.Hotkey = KeyNames[next];
            preferences.Save();
            UpdateAppearance();
        }

        private void ToggleScroll()
        {
            if (running) { StopScroll(null); return; }
            if (preferences.Background && (target == null || selecting))
            {
                string error;
                Native.POINT point;
                if (!Native.GetCursorPos(out point)) { StopScroll("无法获取鼠标位置。"); return; }
                FixedScrollTarget captured = FixedScrollTarget.Capture(point, processId, out error);
                if (captured == null)
                {
                    StopScroll(error);
                    RestoreWindow();
                    return;
                }
                target = captured;
            }
            if (preferences.Background)
            {
                string error;
                if (!target.Validate(out error)) { StopScroll(error); return; }
            }
            selecting = false;
            running = true;
            Native.GetAsyncKeyState((int)Keys.Escape);
            if (!escapeRegistered) escapeRegistered = Native.RegisterHotKey(hotkeys.Handle, 3, 0x4000, (uint)Keys.Escape);
            lastWheel = clock.ElapsedMilliseconds;
            timer.Start();
            UpdateAppearance();
        }

        internal void StopScroll(string reason)
        {
            running = false;
            selecting = false;
            timer.Stop();
            if (escapeRegistered)
            {
                Native.UnregisterHotKey(hotkeys.Handle, 3);
                escapeRegistered = false;
            }
            UpdateAppearance();
            if (reason != null)
            {
                status.Text = reason;
                status.ForeColor = red;
                tips.SetToolTip(status, reason);
            }
        }

        private void UpdateAppearance()
        {
            status.Text = (selecting ? "等待选定区域" : running ? (preferences.Up ? "正在向上滚动" : "正在向下滚动") + "  ·  " + preferences.Speed + " 格 / 秒" : "已停止") + "  ·  " + shortcut.SelectedItem;
            status.ForeColor = running ? green : Color.FromArgb(83, 96, 104);
            tips.SetToolTip(status, status.Text);
            toggle.Text = running ? "停止滚动" : preferences.Background && target == null ? "选定聊天区域" : "开始滚动";
            toggle.BackColor = running ? red : green;
            toggle.AccessibleName = toggle.Text;
            trayStop.Enabled = running;
            tray.Text = "自动滚轮 · " + (running ? "正在滚动" : "已停止") + " · " + shortcut.SelectedItem;
            targetLabel.Text = preferences.Background ? (target == null ? "未选择区域" : target.Name) : "鼠标所在区域";
            tips.SetToolTip(targetLabel, targetLabel.Text);
            pick.Enabled = preferences.Background;
            pick.Text = target == null ? "选择区域" : "重新选择区域";
            trayStop.Enabled = running || selecting;
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!running) return;
            if ((Native.GetAsyncKeyState((int)Keys.Escape) & 0x8000) != 0) { StopScroll(null); return; }
            long now = clock.ElapsedMilliseconds;
            if (preferences.Background)
            {
                if (now - lastWheel < 1000.0 / preferences.Speed) return;
                lastWheel = now;
                string error;
                if (target == null) { StopScroll("未选择滚动区域。"); return; }
                if (!target.Scroll(preferences.Up ? 120 : -120, out error)) StopScroll(error);
                return;
            }
            // Never scroll our own controls, including the speed and shortcut selectors.
            Native.POINT point;
            if (!Native.GetCursorPos(out point)) return;
            uint owner;
            Native.GetWindowThreadProcessId(Native.WindowFromPoint(point), out owner);
            if (owner == (uint)processId) { lastWheel = now; return; }
            if (now - lastWheel < 1000.0 / preferences.Speed) return;
            lastWheel = now;
            Native.INPUT input = new Native.INPUT();
            input.type = 0; // INPUT_MOUSE
            input.mouse.mouseData = unchecked((uint)(preferences.Up ? 120 : -120));
            input.mouse.flags = 0x0800; // MOUSEEVENTF_WHEEL
            if (Native.SendInput(1, new Native.INPUT[] { input }, Marshal.SizeOf(typeof(Native.INPUT))) != 1)
                StopScroll("滚动未发送，已停止。");
        }

        private void HideToTray()
        {
            Hide();
        }

        private void BeginSelect()
        {
            StopScroll(null);
            target = null;
            selecting = true;
            escapeRegistered = Native.RegisterHotKey(hotkeys.Handle, 3, 0x4000, (uint)Keys.Escape);
            UpdateAppearance();
            HideToTray();
        }

        private void RestoreWindow()
        {
            WindowState = FormWindowState.Normal;
            Show();
            Activate();
        }

        private void OnHotkey(int id)
        {
            if (id == 3 && (running || selecting)) StopScroll(null);
            else if (id == registeredId && registeredId != 0) ToggleScroll();
        }

        protected override void WndProc(ref Message message)
        {
            // Lock, sleep, shutdown and desktop changes must never leave scrolling enabled.
            if (message.Msg == 0x02B1 || message.Msg == 0x0011) StopScroll(null);
            base.WndProc(ref message);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            StopScroll(null);
            if (e.CloseReason == CloseReason.UserClosing && !exitRequested)
            {
                e.Cancel = true;
                HideToTray();
                base.OnFormClosing(e);
                return;
            }
            if (registeredId != 0) Native.UnregisterHotKey(hotkeys.Handle, registeredId);
            registeredId = 0;
            tray.Visible = false;
            SystemEventsBridge.Detach();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                tray.Dispose();
                tips.Dispose();
                hotkeys.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class FixedScrollTarget
    {
        private readonly IntPtr root;
        private readonly IntPtr child;
        private readonly uint rootProcess;
        private readonly uint childProcess;
        private readonly Native.POINT childPoint;
        private readonly Native.RECT rootSize;
        private readonly Native.RECT childSize;
        internal readonly string Name;

        private FixedScrollTarget(IntPtr rootWindow, IntPtr childWindow, Native.POINT point)
        {
            root = rootWindow;
            child = childWindow;
            Native.GetWindowThreadProcessId(root, out rootProcess);
            Native.GetWindowThreadProcessId(child, out childProcess);
            Native.GetClientRect(root, out rootSize);
            Native.GetClientRect(child, out childSize);
            Native.ScreenToClient(child, ref point);
            childPoint = point;
            try
            {
                string process = Process.GetProcessById((int)rootProcess).ProcessName;
                Name = process.Equals("WXWork", StringComparison.OrdinalIgnoreCase) ? "企业微信 · 固定区域" : process + " · 固定区域";
            }
            catch (ArgumentException) { Name = "固定窗口"; }
            catch (InvalidOperationException) { Name = "固定窗口"; }
        }

        internal static FixedScrollTarget Capture(Native.POINT point, int ownProcess, out string error)
        {
            return CaptureWindow(Native.WindowFromPoint(point), point, ownProcess, out error);
        }

        internal static FixedScrollTarget CaptureWindow(IntPtr window, Native.POINT point, int ownProcess, out string error)
        {
            error = null;
            if (window == IntPtr.Zero || !Native.IsWindow(window)) { error = "无法选定此区域。"; return null; }
            IntPtr top = Native.GetAncestor(window, 2); // GA_ROOT
            uint owner;
            Native.GetWindowThreadProcessId(top, out owner);
            if (top == IntPtr.Zero || owner == (uint)ownProcess) { error = "请选择聊天记录所在的窗口。"; return null; }
            StringBuilder className = new StringBuilder(256);
            Native.GetClassName(top, className, className.Capacity);
            string windowClass = className.ToString();
            if (windowClass == "Progman" || windowClass == "WorkerW" || windowClass == "Shell_TrayWnd" || windowClass == "Shell_SecondaryTrayWnd")
            { error = "请选择聊天记录所在的窗口。"; return null; }
            Native.POINT local = point;
            Native.RECT area;
            if (!Native.ScreenToClient(window, ref local) || !Native.GetClientRect(window, out area) || !Contains(area, local))
            { error = "请选择窗口内容区域。"; return null; }
            FixedScrollTarget candidate = new FixedScrollTarget(top, window, point);
            if (!candidate.Validate(out error)) return null;
            return candidate;
        }

        internal bool Validate(out string error)
        {
            error = null;
            uint rootOwner, childOwner;
            if (!Native.IsWindow(root) || !Native.IsWindow(child)) { error = "目标窗口已关闭，请重新选择区域。"; return false; }
            Native.GetWindowThreadProcessId(root, out rootOwner);
            Native.GetWindowThreadProcessId(child, out childOwner);
            if (rootOwner != rootProcess || childOwner != childProcess || (child != root && !Native.IsChild(root, child)))
            { error = "目标窗口已变化，请重新选择区域。"; return false; }
            if (Native.IsIconic(root) || !Native.IsWindowVisible(root) || !Native.IsWindowVisible(child))
            { error = "目标已最小化或隐藏，已停止。"; return false; }
            Native.RECT currentRoot, currentChild;
            if (!Native.GetClientRect(root, out currentRoot) || !Native.GetClientRect(child, out currentChild))
            { error = "无法读取目标区域，已停止。"; return false; }
            if (currentRoot.Width != rootSize.Width || currentRoot.Height != rootSize.Height || currentChild.Width != childSize.Width || currentChild.Height != childSize.Height || !Contains(currentChild, childPoint))
            { error = "目标尺寸已变化，请重新选择区域。"; return false; }
            return true;
        }

        internal bool Scroll(int delta, out string error)
        {
            if (!Validate(out error)) return false;
            Native.POINT screen = childPoint;
            if (!Native.ClientToScreen(child, ref screen)) { error = "目标位置不可用，已停止。"; return false; }
            if (screen.x < short.MinValue || screen.x > short.MaxValue || screen.y < short.MinValue || screen.y > short.MaxValue)
            { error = "目标位置超出支持范围，已停止。"; return false; }
            int wheel = unchecked((int)((uint)(ushort)(short)delta << 16));
            int coordinates = unchecked((int)((uint)(ushort)(short)screen.x | ((uint)(ushort)(short)screen.y << 16)));
            UIntPtr result;
            // Send to exactly the selected child. DefWindowProc handles parent propagation;
            // explicitly sending to multiple ancestors would produce duplicate scrolling.
            IntPtr sent = Native.SendMessageTimeout(child, 0x020A, new IntPtr(wheel), new IntPtr(coordinates), 0x0003, 100, out result);
            if (sent == IntPtr.Zero)
            {
                error = Marshal.GetLastWin32Error() == 5 ? "目标权限较高，滚动未发送。" : "目标未响应，已停止。";
                return false;
            }
            error = null;
            return true;
        }

        private static bool Contains(Native.RECT area, Native.POINT point)
        { return point.x >= area.left && point.x < area.right && point.y >= area.top && point.y < area.bottom; }
    }

    // A message-only window keeps the global hotkeys alive independently of the visible form.
    internal sealed class HotkeyReceiver : NativeWindow, IDisposable
    {
        internal const string WindowName = "AutoScrollWheel.HotkeyWindow.8A64BB31";
        internal const uint RestoreMessage = 0x8011;
        internal event Action<int> Pressed;
        internal event Action RestoreRequested;
        internal HotkeyReceiver()
        {
            CreateParams parameters = new CreateParams();
            parameters.Caption = WindowName;
            parameters.Parent = new IntPtr(-3); // HWND_MESSAGE
            CreateHandle(parameters);
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0312 && Pressed != null)
            {
                Pressed(message.WParam.ToInt32());
                return;
            }
            if (message.Msg == RestoreMessage && RestoreRequested != null)
            {
                RestoreRequested();
                return;
            }
            base.WndProc(ref message);
        }
        public void Dispose() { DestroyHandle(); }
    }

    internal static class SystemEventsBridge
    {
        private static ScrollForm form;
        internal static void Attach(ScrollForm value)
        {
            form = value;
            Microsoft.Win32.SystemEvents.SessionSwitch += OnSession;
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPower;
        }
        private static void Stop()
        {
            ScrollForm current = form;
            if (current == null || current.IsDisposed || !current.IsHandleCreated) return;
            try { current.BeginInvoke((Action)delegate { if (!current.IsDisposed) current.StopScroll(null); }); }
            catch (InvalidOperationException) { }
        }
        private static void OnSession(object sender, Microsoft.Win32.SessionSwitchEventArgs e) { Stop(); }
        private static void OnPower(object sender, Microsoft.Win32.PowerModeChangedEventArgs e) { Stop(); }
        internal static void Detach()
        {
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSession;
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPower;
            form = null;
        }
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct RECT
        {
            internal int left, top, right, bottom;
            internal int Width { get { return right - left; } }
            internal int Height { get { return bottom - top; } }
        }
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsChild(IntPtr parent, IntPtr child);
        [DllImport("user32.dll")]
        internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(IntPtr window, StringBuilder name, int length);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetClientRect(IntPtr window, out RECT rectangle);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ScreenToClient(IntPtr window, ref POINT point);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ClientToScreen(IntPtr window, ref POINT point);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string windowName);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT { internal int x; internal int y; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT
        {
            internal int dx, dy;
            internal uint mouseData, flags, time;
            internal UIntPtr extraInfo;
        }
        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT { internal uint type; internal MOUSEINPUT mouse; }
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll")]
        internal static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern uint SendInput(uint count, INPUT[] inputs, int size);
    }
}
