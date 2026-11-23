using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DisplayDock
{
    /// <summary>
    /// 主界面（显示器切换器 v1.0）：
    /// 1) 自动检测显示器的输入口，“本机端口”显示正在连接的那一个，“切换屏幕端口”可自选；
    /// 2) 一键或快捷键在“本机端口 / 切换屏幕端口”之间来回切；
    /// 3) 自定义切换快捷键（点击后直接按键录入）。
    /// 程序只切换显示器的画面输入，不使用任何网络连接。
    /// </summary>
    public sealed class MainForm : Form
    {
        private sealed class ComboPort
        {
            public uint Value;
            public string Text;
            public override string ToString() { return Text; }
        }

        private readonly AppService service;
        private bool loading;
        private bool capturing;
        private bool exiting;
        private bool balloonShown;
        private string captureBackup = "";

        private Label monitorLabel;
        private Label currentLabel;
        private FlatCombo localPortBox;
        private FlatCombo targetPortBox;
        private FlatButton useCurrentButton;
        private FlatButton refreshButton;

        private ToggleSwitch hotkeyCheck;
        private InputShell hotkeyBox;
        private FlatButton captureButton;

        private Label statusLabel;
        private Label targetLabel;
        private LogShell logShell;
        private TextBox logBox;
        private FlatButton switchButton;
        private FlatButton hideButton;
        private NotifyIcon tray;
        private Timer uiTimer;

        public MainForm(AppService service)
        {
            this.service = service;
            Text = "显示器切换器 v1.0";
            BackColor = Ui.Bg;
            ClientSize = new Size(980, 604);
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(996, 652);
            MaximumSize = new Size(996, 652);
            MaximizeBox = false;
            DoubleBuffered = true;
            Font = Ui.Body(9.25F, FontStyle.Regular);
            TrySetIcon();
            BuildUi();
            BuildTray();
            LoadFromSettings();
            service.Changed += OnServiceChanged;
            uiTimer = new Timer();
            uiTimer.Interval = 1000;
            uiTimer.Tick += delegate { UpdateUi(); };
            uiTimer.Start();
            UpdateUi();
            service.Log.Write("界面已就绪。按 " + service.Settings.Hotkey + " 可在两台电脑之间来回切换。");
        }

        // ---------- 界面搭建 ----------

        /// <summary>用代码画一个和界面同色的窗口/托盘图标，不依赖外部 .ico 文件。</summary>
        private void TrySetIcon()
        {
            try
            {
                using (var bmp = new Bitmap(32, 32))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    Ui.FillRound(g, new Rectangle(0, 0, 32, 32), 8, Ui.Accent);
                    TextRenderer.DrawText(g, "⇄", Ui.Body(13F, FontStyle.Bold), new Rectangle(0, 0, 32, 32), Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    IntPtr handle = bmp.GetHicon();
                    Icon = Icon.FromHandle(handle);
                }
            }
            catch (Exception) { }
        }

        private void BuildUi()
        {
            BuildHeader();

            // ---- 显示器与输入端口 ----
            var monitorCard = AddCard(84, 176);

            monitorLabel = AddLabel(monitorCard, "", 24, 48, 800, 20, 9.25F, FontStyle.Regular, Ui.Muted);

            AddLabel(monitorCard, "当前画面输入", 24, 78, 110, 22, 9.25F, FontStyle.Regular, Ui.Muted);
            currentLabel = AddLabel(monitorCard, "", 132, 74, 620, 26, 12.5F, FontStyle.Bold, Ui.Accent);

            AddLabel(monitorCard, "本机端口", 24, 112, 120, 22, 9.25F, FontStyle.Regular, Ui.Text);
            localPortBox = AddCombo(monitorCard, 152, 108, 250);
            localPortBox.SelectedIndexChanged += delegate { OnPortChanged(); };
            useCurrentButton = AddButton(monitorCard, "用当前画面识别", 414, 106, 132, 30, FlatButton.BtnStyle.Secondary);
            useCurrentButton.Click += delegate { UseCurrentAsLocal(); };

            AddLabel(monitorCard, "切换屏幕端口", 24, 142, 120, 22, 9.25F, FontStyle.Regular, Ui.Text);
            targetPortBox = AddCombo(monitorCard, 152, 138, 250);
            targetPortBox.SelectedIndexChanged += delegate { OnPortChanged(); };

            refreshButton = AddButton(monitorCard, "重新检测", 828, 12, 96, 30, FlatButton.BtnStyle.Secondary);
            refreshButton.Click += delegate { DoRefresh(); };
            AddCardTitle(monitorCard, "显示器与输入端口");

            // ---- 快捷键 ----
            var hotkeyCard = AddCard(272, 80);
            AddCardTitle(hotkeyCard, "切换快捷键");

            hotkeyCheck = new ToggleSwitch();
            hotkeyCheck.Text = "启用快捷键切换";
            hotkeyCheck.SetBounds(24, 42, 180, 24);
            hotkeyCheck.CheckedChanged += delegate { OnHotkeyToggled(); };
            hotkeyCard.Controls.Add(hotkeyCheck);

            hotkeyBox = AddInput(hotkeyCard, "", true, 216, 40, 220, 30);
            captureButton = AddButton(hotkeyCard, "点击后按新组合键", 446, 38, 170, 32, FlatButton.BtnStyle.Secondary);
            captureButton.Click += delegate { BeginCapture(); };

            // ---- 状态与日志 ----
            var stateCard = AddCard(364, 216);
            AddCardTitle(stateCard, "运行状态");

            switchButton = AddButton(stateCard, "立即切换", 24, 44, 190, 52, FlatButton.BtnStyle.Primary);
            switchButton.Font = Ui.Body(12F, FontStyle.Bold);
            switchButton.Click += delegate { DoSwitch(); };

            hideButton = AddButton(stateCard, "隐藏到托盘", 226, 54, 116, 32, FlatButton.BtnStyle.Secondary);
            hideButton.Click += delegate { HideToTray(true); };

            statusLabel = AddLabel(stateCard, "", 360, 44, 550, 24, 10.5F, FontStyle.Bold, Ui.Text);
            targetLabel = AddLabel(stateCard, "", 360, 72, 550, 22, 9.25F, FontStyle.Regular, Ui.Muted);

            logShell = new LogShell();
            logShell.SetBounds(24, 106, 892, 100);
            stateCard.Controls.Add(logShell);
            logBox = logShell.Inner;
        }

        private void BuildHeader()
        {
            var header = new Panel();
            header.BackColor = Color.White;
            header.SetBounds(0, 0, 980, 72);
            Controls.Add(header);

            var line = new Panel();
            line.BackColor = Ui.Border;
            line.SetBounds(0, 71, 980, 1);
            header.Controls.Add(line);

            var icon = new Panel();
            icon.SetBounds(20, 14, 44, 44);
            icon.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Ui.FillRound(e.Graphics, new Rectangle(0, 0, 43, 43), 11, Ui.Accent);
                TextRenderer.DrawText(e.Graphics, "⇄", Ui.Body(17F, FontStyle.Bold), new Rectangle(0, 0, 43, 43), Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            header.Controls.Add(icon);

            var title = new Label();
            title.Text = "显示器切换器";
            title.Font = Ui.Body(15F, FontStyle.Bold);
            title.ForeColor = Ui.Text;
            title.SetBounds(78, 20, 400, 28);
            header.Controls.Add(title);

            var version = new Label();
            version.Text = "v1.0";
            version.Font = Ui.Body(9F, FontStyle.Regular);
            version.ForeColor = Ui.Faint;
            version.TextAlign = ContentAlignment.MiddleRight;
            version.SetBounds(760, 26, 200, 20);
            header.Controls.Add(version);
        }

        private CardPanel AddCard(int y, int height)
        {
            var card = new CardPanel();
            card.SetBounds(20, y, 940, height);
            Controls.Add(card);
            return card;
        }

        private static void AddCardTitle(CardPanel card, string text)
        {
            var bar = new Panel();
            bar.BackColor = Ui.Accent;
            bar.SetBounds(14, 20, 3, 15);
            card.Controls.Add(bar);

            var title = new Label();
            title.Text = text;
            title.Font = Ui.Body(10F, FontStyle.Bold);
            title.ForeColor = Ui.Text;
            title.SetBounds(24, 18, 400, 20);
            card.Controls.Add(title);
        }

        private static Label AddLabel(Control parent, string text, int x, int y, int w, int h,
                                      float size, FontStyle style, Color color)
        {
            var label = new Label();
            label.Text = text;
            label.Font = Ui.Body(size, style);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.SetBounds(x, y, w, h);
            parent.Controls.Add(label);
            return label;
        }

        private static FlatCombo AddCombo(Control parent, int x, int y, int w)
        {
            var combo = new FlatCombo();
            combo.SetBounds(x, y, w, 26);
            parent.Controls.Add(combo);
            return combo;
        }

        private static InputShell AddInput(Control parent, string text, bool readOnly, int x, int y, int w, int h)
        {
            var shell = new InputShell(text, readOnly);
            shell.SetBounds(x, y, w, h);
            parent.Controls.Add(shell);
            return shell;
        }

        private static FlatButton AddButton(Control parent, string text, int x, int y, int w, int h, FlatButton.BtnStyle style)
        {
            var button = new FlatButton();
            button.Text = text;
            button.Style = style;
            button.SetBounds(x, y, w, h);
            parent.Controls.Add(button);
            return button;
        }

        private void BuildTray()
        {
            tray = new NotifyIcon();
            tray.Icon = Icon == null ? SystemIcons.Application : Icon;
            tray.Text = "显示器切换器";
            tray.Visible = false;
            var menu = new ContextMenuStrip();
            menu.Items.Add("显示主界面", null, delegate { ShowFromTray(); });
            menu.Items.Add("立即切换", null, delegate { DoSwitch(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { ExitApp(); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowFromTray(); };
        }

        // ---------- 初始值 ----------

        private void LoadFromSettings()
        {
            loading = true;
            try
            {
                AppSettings s = service.Settings;
                hotkeyCheck.Checked = s.HotkeyEnabled;
                hotkeyBox.Text = s.Hotkey;
            }
            finally { loading = false; }
        }

        // ---------- 事件处理 ----------

        private void DoRefresh()
        {
            Cursor = Cursors.WaitCursor;
            try { service.Refresh(); }
            finally { Cursor = Cursors.Default; }
            UpdateUi();
        }

        private void OnPortChanged()
        {
            if (loading) return;
            ComboPort local = localPortBox.SelectedItem as ComboPort;
            ComboPort target = targetPortBox.SelectedItem as ComboPort;
            if (local == null || target == null) return;
            if (target.Value == 0) return;   // 切换端口还没选，不必写配置
            if (target.Value == local.Value)
            {
                MessageBox.Show(this, "“切换屏幕端口”不能和“本机端口”相同，请重新选择。", "端口设置",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                UpdateUi();
                return;
            }
            service.UpdatePorts(local.Value, target.Value);
        }

        private void UseCurrentAsLocal()
        {
            MonitorSnapshot snapshot = service.Snapshot;
            if (snapshot == null || !snapshot.CurrentInput.HasValue)
            {
                MessageBox.Show(this, "暂时读不到显示器当前输入口。请确认显示器已开启 DDC/CI，然后点“重新检测”。",
                    "识别本机端口", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            uint value = snapshot.CurrentInput.Value;
            uint other = 0;
            if (snapshot.Ports.Count > 0)
            {
                foreach (PortInfo port in snapshot.Ports)
                {
                    if (port.Value != value) { other = port.Value; break; }
                }
            }
            service.UpdatePorts(value, other);
            UpdateUi();
        }

        private void OnHotkeyToggled()
        {
            if (loading) return;
            service.UpdateHotkey(hotkeyCheck.Checked, hotkeyBox.Text.Trim());
        }

        private void BeginCapture()
        {
            if (capturing)
            {
                capturing = false;
                hotkeyBox.Text = captureBackup;
                captureButton.Text = "点击后按新组合键";
                return;
            }
            captureBackup = hotkeyBox.Text;
            capturing = true;
            hotkeyBox.Text = "";
            captureButton.Text = "按下组合键…（Esc 取消）";
            Activate();
            Focus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (capturing)
            {
                Keys key = keyData & Keys.KeyCode;
                if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin)
                    return true;
                if (key == Keys.Escape)
                {
                    BeginCapture();
                    return true;
                }
                Keys modifiers = keyData & Keys.Modifiers;
                var spec = new HotkeySpec();
                spec.Ctrl = (modifiers & Keys.Control) == Keys.Control;
                spec.Alt = (modifiers & Keys.Alt) == Keys.Alt;
                spec.Shift = (modifiers & Keys.Shift) == Keys.Shift;
                spec.Win = (modifiers & Keys.LWin) == Keys.LWin || (modifiers & Keys.RWin) == Keys.RWin;
                spec.Vk = (int)key;
                if (!spec.Ctrl && !spec.Alt && !spec.Shift && !spec.Win)
                {
                    MessageBox.Show(this, "快捷键必须包含 Ctrl/Alt/Shift/Win 中的至少一个，请重新按。", "快捷键",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return true;
                }
                string text = HotkeyText.Format(spec);
                HotkeySpec parsed;
                string parseError;
                if (!HotkeyText.TryParse(text, out parsed, out parseError))
                {
                    MessageBox.Show(this, "这个按键不能用作快捷键（" + parseError + "），请换一个，推荐 F 键或字母。", "快捷键",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return true;
                }
                capturing = false;
                hotkeyBox.Text = text;
                captureButton.Text = "点击后按新组合键";
                hotkeyCheck.Checked = true;
                service.UpdateHotkey(true, text);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void DoSwitch()
        {
            Cursor = Cursors.WaitCursor;
            try { service.ToggleSwitch(); }
            catch (Exception ex)
            {
                service.Log.Write("切换异常：" + ex.Message);
                MessageBox.Show(this, "切换失败：" + ex.Message, "切换", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { Cursor = Cursors.Default; }
            UpdateUi();
        }

        // ---------- 状态刷新 ----------

        private void OnServiceChanged()
        {
            if (IsDisposed) return;
            try
            {
                if (InvokeRequired) BeginInvoke(new Action(UpdateUi));
                else UpdateUi();
            }
            catch (Exception) { }
        }

        private void UpdateUi()
        {
            if (loading || IsDisposed) return;
            loading = true;
            try
            {
                AppSettings s = service.Settings;
                MonitorSnapshot snapshot = service.Snapshot;

                if (snapshot == null)
                {
                    monitorLabel.Text = "显示器：检测中…";
                    monitorLabel.ForeColor = Ui.Muted;
                }
                else if (snapshot.Error != null && snapshot.Error.Length > 0)
                {
                    monitorLabel.Text = "显示器：" + (String.IsNullOrEmpty(snapshot.MonitorName) ? "未识别" : snapshot.MonitorName) +
                        "（DDC 未响应，已列出常见输入口，请手动确认；详细原因见日志）";
                    monitorLabel.ForeColor = Ui.Warn;
                }
                else
                {
                    monitorLabel.Text = "显示器：" + snapshot.MonitorName + "　·　DDC " + (snapshot.Ready ? "可用" : "未就绪") +
                        "　·　" + snapshot.Ports.Count + " 个输入口";
                    monitorLabel.ForeColor = Ui.Muted;
                }

                if (snapshot != null)
                {
                    bool hasCurrent = snapshot.CurrentInput.HasValue;
                    currentLabel.Text = hasCurrent
                        ? snapshot.CurrentName
                        : "读取失败（DDC 未响应；拔插显示器电源/视频线后点“重新检测”）";
                    currentLabel.ForeColor = hasCurrent ? Ui.Accent : Ui.Warn;
                    FillPortCombo(localPortBox, snapshot, s.LocalInput);
                    FillPortCombo(targetPortBox, snapshot, s.TargetInput);
                }
                else
                {
                    currentLabel.Text = "读取失败";
                    currentLabel.ForeColor = Ui.Warn;
                }

                if (capturing) { }
                else if (hotkeyBox.Text != s.Hotkey) hotkeyBox.Text = s.Hotkey;
                hotkeyCheck.Checked = s.HotkeyEnabled;

                statusLabel.Text = "当前画面：" + (snapshot != null && !snapshot.CurrentInput.HasValue
                        ? "读不到（DDC 未响应）"
                        : (service.LocalDisplayed ? "本机正在显示" : "另一台电脑正在显示"));
                statusLabel.ForeColor = (snapshot != null && snapshot.CurrentInput.HasValue)
                    ? (service.LocalDisplayed ? Ui.Text : Ui.Accent)
                    : Ui.Warn;

                targetLabel.Text = "切换目标：" + PortText(snapshot, s.TargetInput) +
                    "　　快捷键：" + (s.HotkeyEnabled ? s.Hotkey : "未启用");

                string[] lines = service.Log is ListLogSink ? ((ListLogSink)service.Log).Snapshot() : new string[0];
                string text = String.Join(Environment.NewLine, lines);
                if (logBox.Text != text)
                {
                    logBox.Text = text;
                    logBox.SelectionStart = logBox.TextLength;
                    logBox.ScrollToCaret();
                }

                if (s.LocalInput == 0 || s.TargetInput == 0) useCurrentButton.Enabled = true;
            }
            finally { loading = false; }
        }

        private string PortText(MonitorSnapshot snapshot, uint value)
        {
            if (value == 0) return "未设置";
            if (snapshot != null)
            {
                PortInfo port = snapshot.Find(value);
                if (port != null) return port.Text;
            }
            return PortCatalog.Label(value) + "（0x" + value.ToString("X2") + "）";
        }

        private void FillPortCombo(ComboBox combo, MonitorSnapshot snapshot, uint selected)
        {
            var items = new List<ComboPort>();
            if (selected == 0)
            {
                // 还没设置过端口时给一个占位项，避免下拉框一片空白让人以为界面坏了。
                var placeholder = new ComboPort();
                placeholder.Value = 0;
                placeholder.Text = "（请选择）";
                items.Add(placeholder);
            }
            foreach (PortInfo port in snapshot.Ports)
            {
                var item = new ComboPort();
                item.Value = port.Value;
                item.Text = port.Text;
                items.Add(item);
            }
            if (selected != 0 && snapshot.Find(selected) == null)
            {
                var item = new ComboPort();
                item.Value = selected;
                item.Text = PortCatalog.Label(selected) + "（0x" + selected.ToString("X2") + "，已不在能力列表）";
                items.Add(item);
            }
            bool same = combo.Items.Count == items.Count;
            if (same)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    ComboPort existing = combo.Items[i] as ComboPort;
                    if (existing == null || existing.Value != items[i].Value || existing.Text != items[i].Text) { same = false; break; }
                }
            }
            if (!same)
            {
                combo.Items.Clear();
                foreach (ComboPort item in items) combo.Items.Add(item);
            }
            for (int i = 0; i < combo.Items.Count; i++)
            {
                ComboPort item = combo.Items[i] as ComboPort;
                if (item != null && item.Value == selected)
                {
                    if (combo.SelectedIndex != i) combo.SelectedIndex = i;
                    return;
                }
            }
        }

        // ---------- 窗口/托盘 ----------

        private void HideToTray(bool showBalloon)
        {
            Hide();
            if (tray != null)
            {
                tray.Visible = true;
                if (showBalloon && !balloonShown)
                {
                    balloonShown = true;
                    tray.ShowBalloonTip(2000, "显示器切换器", "程序仍在后台运行，快捷键依然有效。双击托盘图标可重新打开。", ToolTipIcon.Info);
                }
            }
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            if (tray != null) tray.Visible = false;
            UpdateUi();
        }

        private void ExitApp()
        {
            exiting = true;
            Close();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized) HideToTray(true);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting)
            {
                e.Cancel = true;
                HideToTray(true);
                return;
            }
            if (uiTimer != null) { uiTimer.Stop(); uiTimer.Dispose(); uiTimer = null; }
            if (tray != null)
            {
                tray.Visible = false;
                tray.Dispose();
                tray = null;
            }
            base.OnFormClosing(e);
        }
    }
}
