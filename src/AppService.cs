using System;
using System.IO;
using System.Threading;

namespace DisplayDock
{
    /// <summary>
    /// 程序大脑：显示器 DDC/CI 会话 + 全局快捷键 + 切换校验。
    ///
    /// 核心规则：
    /// - 每台电脑在显示器上占一个输入口：本机端口 LocalInput / 切换端口 TargetInput。
    /// - 切换 = 把显示器切到“另一台”的输入口；再按一次切回来。
    /// - 本程序只切换显示器的画面输入，不使用任何网络连接。
    /// </summary>
    public sealed class AppService : IDisposable
    {
        private readonly object gate = new object();
        private readonly string baseDirectory;
        private readonly string configPath;
        private readonly ListLogSink log = new ListLogSink();
        private readonly MonitorService monitor;
        private readonly HotkeyRegistrar hotkeys;
        private AppSettings settings;
        private MonitorSnapshot snapshot;
        private bool localDisplayed;
        private bool disposed;
        private Timer verifyTimer;
        private uint pendingTarget;
        private int verifyAttempt;

        public AppService(string baseDirectory)
        {
            this.baseDirectory = Path.GetFullPath(baseDirectory);
            this.configPath = Path.Combine(this.baseDirectory, ConfigFile.FileName);
            monitor = new MonitorService(log);
            hotkeys = new HotkeyRegistrar(log);
            hotkeys.Pressed += OnHotkeyPressed;
        }

        public ILogSink Log { get { return log; } }
        public string ConfigPath { get { return configPath; } }

        public AppSettings Settings { get { lock (gate) { return settings; } } }
        public MonitorSnapshot Snapshot { get { lock (gate) { return snapshot; } } }
        public bool LocalDisplayed { get { lock (gate) { return localDisplayed; } } }

        /// <summary>任何状态变化（后台线程触发，界面需 Invoke）。</summary>
        public event Action Changed;

        private void RaiseChanged()
        {
            Action handler = Changed;
            if (handler == null) return;
            try { handler(); }
            catch (Exception) { }
        }

        private void Note(string text)
        {
            log.Write(text);
            RaiseChanged();
        }

        // ---------- 启动 ----------

        public void Initialize()
        {
            lock (gate)
            {
                LoadSettingsLocked();
                string problem = settings.Validate();
                if (problem != null) log.Write("配置检查：" + problem);
                RescanLocked(true);
                RestoreDisplayedStateLocked();
                ApplyHotkeyLocked();
            }
            RaiseChanged();
        }

        /// <summary>读取配置；不存在或损坏时使用默认值。</summary>
        private void LoadSettingsLocked()
        {
            settings = ConfigFile.ReadOrDefault<AppSettings>(configPath);
            if (settings == null) settings = new AppSettings();
            if (settings.Version != AppSettings.CurrentVersion)
            {
                settings.Version = AppSettings.CurrentVersion;
                ConfigFile.Write(configPath, settings);
            }
        }

        /// <summary>重新枚举显示器并读端口/当前输入（不抛出）。</summary>
        public void Refresh()
        {
            lock (gate)
            {
                RescanLocked(false);
            }
            RaiseChanged();
        }

        private void RescanLocked(bool initial)
        {
            string preferred = settings == null ? "" : settings.MonitorKey;
            snapshot = monitor.Scan(preferred);
            if (snapshot == null) return;
            if (!String.IsNullOrWhiteSpace(snapshot.MonitorKey) && settings.MonitorKey != snapshot.MonitorKey)
            {
                settings.MonitorKey = snapshot.MonitorKey;
                if (!initial) ConfigFile.Write(configPath, settings);
            }
            if (snapshot.Error != null && snapshot.Error.Length > 0) Note("显示器提示：" + snapshot.Error);
            if (!snapshot.Ready || !snapshot.CurrentInput.HasValue)
            {
                log.Write("读不到 DDC/CI 时：请在显示器 OSD 里确认 DDC/CI 已开启，或拔插显示器电源/视频线后点“重新检测”；" +
                          "部分显示器在待机、长时间未读取或刚切换画面后会短暂不响应。");
            }
            if (snapshot.Ports.Count > 0)
            {
                bool localValid = false, targetValid = false;
                foreach (PortInfo port in snapshot.Ports)
                {
                    if (port.Value == settings.LocalInput) localValid = true;
                    if (port.Value == settings.TargetInput) targetValid = true;
                }
                if (initial && !localValid && snapshot.CurrentInput.HasValue)
                {
                    settings.LocalInput = snapshot.CurrentInput.Value;
                    log.Write("自动识别本机端口为 " + PortCatalog.Label(settings.LocalInput) + "（0x" + settings.LocalInput.ToString("X2") + "）");
                }
                if (!targetValid || settings.TargetInput == settings.LocalInput)
                {
                    foreach (PortInfo port in snapshot.Ports)
                    {
                        if (port.Value != settings.LocalInput) { settings.TargetInput = port.Value; break; }
                    }
                }
            }
            if (!initial) ConfigFile.Write(configPath, settings);
        }

        private void RestoreDisplayedStateLocked()
        {
            if (snapshot != null && snapshot.CurrentInput.HasValue && settings.LocalInput != 0)
            {
                localDisplayed = snapshot.CurrentInput.Value == settings.LocalInput;
                log.Write("当前显示器输入：" + snapshot.CurrentName + "；" + (localDisplayed ? "本机正在显示" : "另一台电脑正在显示"));
                return;
            }
            // 读不到 DDC 时无法判断画面归属，按“本机在显示”处理，只影响状态文字，不影响切换。
            localDisplayed = true;
            log.Write("无法读取当前输入口，画面归属未知（DDC/CI 未响应）。");
        }

        // ---------- 快捷键 ----------

        private void ApplyHotkeyLocked()
        {
            if (settings == null) return;
            if (!settings.HotkeyEnabled)
            {
                hotkeys.Unregister();
                return;
            }
            HotkeySpec spec;
            string error;
            if (!HotkeyText.TryParse(settings.Hotkey, out spec, out error))
            {
                Note("快捷键配置无效：" + error);
                return;
            }
            string registerError;
            if (!hotkeys.Register(spec, out registerError) && registerError != null) Note(registerError);
        }

        public void UpdateHotkey(bool enabled, string text)
        {
            string error = null;
            lock (gate)
            {
                if (settings == null) return;
                if (enabled)
                {
                    HotkeySpec spec;
                    if (!HotkeyText.TryParse(text, out spec, out error))
                    {
                        // 保持原样，不做修改
                    }
                }
                if (error == null)
                {
                    settings.HotkeyEnabled = enabled;
                    settings.Hotkey = text;
                    ConfigFile.Write(configPath, settings);
                    ApplyHotkeyLocked();
                }
            }
            if (error != null) Note("快捷键未修改：" + error);
            RaiseChanged();
        }

        private void OnHotkeyPressed()
        {
            try { ToggleSwitch(); }
            catch (Exception ex) { Note("快捷键处理异常：" + ex.Message); }
        }

        // ---------- 端口设置 ----------

        /// <summary>用户手动选择显示器（按 Display.Key）。</summary>
        public void UpdateMonitor(string monitorKey)
        {
            lock (gate)
            {
                if (settings == null) return;
                settings.MonitorKey = monitorKey == null ? "" : monitorKey;
                ConfigFile.Write(configPath, settings);
                RescanLocked(false);
            }
            RaiseChanged();
        }

        public void UpdatePorts(uint localInput, uint targetInput)
        {
            lock (gate)
            {
                if (targetInput != 0 && targetInput == localInput)
                {
                    Note("切换端口不能和本机端口相同。");
                    return;
                }
                settings.LocalInput = localInput;
                settings.TargetInput = targetInput;
                if (snapshot != null && snapshot.CurrentInput.HasValue)
                    localDisplayed = snapshot.CurrentInput.Value == localInput;
                ConfigFile.Write(configPath, settings);
                Note("已保存端口设置：本机 " + Describe(localInput) + "，切换 " + Describe(targetInput));
            }
            RaiseChanged();
        }

        private string Describe(uint value)
        {
            if (value == 0) return "未设置";
            return PortCatalog.Label(value) + "（0x" + value.ToString("X2") + "）";
        }

        // ---------- 切换 ----------

        /// <summary>快捷键/按钮入口：在“本机端口”和“切换端口”之间来回切。</summary>
        public void ToggleSwitch()
        {
            lock (gate)
            {
                if (settings == null) return;
                if (settings.LocalInput == 0 || settings.TargetInput == 0)
                {
                    Note("请先在界面里设置“本机端口”和“切换屏幕端口”。");
                    return;
                }
                uint current;
                string error;
                bool readable = monitor.TryReadCurrent(out current, out error);
                uint wanted;
                if (readable && current == settings.LocalInput) wanted = settings.TargetInput;
                else if (readable && current == settings.TargetInput) wanted = settings.LocalInput;
                else wanted = localDisplayed ? settings.TargetInput : settings.LocalInput;
                log.Write("开始切换：当前 " + (readable ? Describe(current) : "读取失败") + " → 目标 " + Describe(wanted));
                ExecuteSwitchLocked(wanted);
            }
            RaiseChanged();
        }

        /// <summary>执行一次切换：写显示器 DDC 输入源，并安排回读校验。</summary>
        private void ExecuteSwitchLocked(uint target)
        {
            string error;
            if (monitor.TryWriteInput(target, out error))
            {
                Note("已提交切换指令：" + Describe(target) + "（正在校验是否生效）");
                AfterSwitchLocked(target);
                return;
            }
            Note("切换失败：" + error + "。请确认显示器支持 DDC/CI 并已在 OSD 中开启，必要时拔插显示器电源/视频线后点“重新检测”。");
        }

        private void AfterSwitchLocked(uint targetPort)
        {
            bool weShow = settings.LocalInput != 0 && targetPort == settings.LocalInput;
            localDisplayed = weShow;
            if (snapshot != null) snapshot.CurrentInput = targetPort;
            ScheduleVerify(targetPort);
        }

        private void ScheduleVerify(uint target)
        {
            pendingTarget = target;
            verifyAttempt = 0;
            if (verifyTimer != null) { verifyTimer.Dispose(); verifyTimer = null; }
            verifyTimer = new Timer(VerifyAfterSwitch, null, 1800, Timeout.Infinite);
        }

        /// <summary>
        /// 切换后回读显示器确认是否真的切过去了。有些显示器会“接受”DDC 写入却不执行
        /// （典型情况：目标输入口没有接设备），所以必须回读校验，不能只看写入是否成功。
        /// </summary>
        private void VerifyAfterSwitch(object state)
        {
            try
            {
                lock (gate)
                {
                    if (disposed) return;
                    uint value;
                    string error;
                    if (monitor.TryReadCurrent(out value, out error))
                    {
                        bool weShow = settings.LocalInput != 0 && value == settings.LocalInput;
                        if (value == pendingTarget)
                        {
                            log.Write("切换已生效：显示器当前输入 " + Describe(value) + "，" + (weShow ? "本机正在显示" : "另一台电脑正在显示"));
                        }
                        else
                        {
                            log.Write("切换未见效：要求切到 " + Describe(pendingTarget) + "，回读仍是 " + Describe(value)
                                + "。如果目标输入口空着（没接设备或对方没输出信号），显示器会在几秒后自动回到有信号的输入口，"
                                + "这属于显示器的保护行为；请确认对方电脑已开机、视频线已接好再试。");
                        }
                        localDisplayed = weShow;
                        snapshot = monitor.Snapshot;
                    }
                    else
                    {
                        verifyAttempt++;
                        if (verifyAttempt < 3)
                        {
                            log.Write("切换后暂时读不到显示器（" + error + "），稍后重试校验（" + verifyAttempt + "/3）。");
                            if (verifyTimer != null) { verifyTimer.Dispose(); verifyTimer = null; }
                            verifyTimer = new Timer(VerifyAfterSwitch, null, 2200, Timeout.Infinite);
                            RaiseChanged();
                            return;
                        }
                        log.Write("切换后 3 次都读不到显示器（" + error + "）。可能是 DDC/CI 通道卡住："
                            + "如果画面并没有切过去，请给显示器断电重开（或拔插视频线）后再点“重新检测”。");
                    }
                }
            }
            catch (Exception ex)
            {
                log.Write("切换后校验异常：" + ex.Message);
            }
            RaiseChanged();
        }

        // ---------- 退出 ----------

        public void Shutdown()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                if (verifyTimer != null) { verifyTimer.Dispose(); verifyTimer = null; }
                hotkeys.Dispose();
                monitor.Dispose();
                if (settings != null) ConfigFile.Write(configPath, settings);
            }
        }

        public void Dispose()
        {
            Shutdown();
        }
    }
}
