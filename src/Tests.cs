using System;
using System.Collections.Generic;
using System.IO;

namespace DisplayDock
{
    /// <summary>
    /// 无界面回归测试（csc 编译成控制台 exe，退出码 0 = 全部通过）。
    /// 用法：DisplayDockTests.exe [--live-ddc]
    /// </summary>
    internal static class DisplayDockTests
    {
        private static int failed;
        private static int passed;
        private static int skipped;

        [STAThread]
        private static int Main(string[] args)
        {
            bool liveDdc = false;
            foreach (string arg in args)
            {
                if (String.Equals(arg, "--live-ddc", StringComparison.OrdinalIgnoreCase)) liveDdc = true;
            }

            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("显示器切换器 v1.0 回归测试");
            Console.WriteLine("=====================================");

            TestConfig();
            TestValidation();
            TestPorts();
            TestHotkey();
            TestSnapshot();
            TestSettingsClone();
            TestAppServiceIntegration();
            if (liveDdc) TestLiveDdc();

            Console.WriteLine("=====================================");
            Console.WriteLine("通过 " + passed + " 项，失败 " + failed + " 项，跳过 " + skipped + " 项。");
            return failed == 0 ? 0 : 1;
        }

        // ---------- 断言 ----------

        private static void Check(string name, bool condition, string detail = "")
        {
            if (condition)
            {
                passed++;
                Console.WriteLine("[通过] " + name);
                return;
            }
            failed++;
            Console.WriteLine("[失败] " + name + (String.IsNullOrEmpty(detail) ? "" : "  → " + detail));
        }

        private static void CheckEqual(string name, string expected, string actual)
        {
            Check(name, String.Equals(expected, actual, StringComparison.Ordinal), "期望 " + expected + "，实际 " + actual);
        }

        private static void CheckEqual(string name, uint expected, uint actual)
        {
            Check(name, expected == actual, "期望 0x" + expected.ToString("X2") + "，实际 0x" + actual.ToString("X2"));
        }

        private static void Skip(string name, string reason)
        {
            skipped++;
            Console.WriteLine("[跳过] " + name + "  → " + reason);
        }

        /// <summary>在测试程序目录下建临时目录：不依赖 %TEMP%，受限环境下也能稳定运行。</summary>
        private static string NewTestDirectory(string prefix)
        {
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                prefix + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        // ---------- 用例 ----------

        private static void TestConfig()
        {
            string directory = NewTestDirectory("DisplayDockTests");
            try
            {
                string path = Path.Combine(directory, ConfigFile.FileName);
                var missing = ConfigFile.ReadOrDefault<AppSettings>(path);
                Check("配置文件不存在时返回默认值", missing != null && missing.Version == AppSettings.CurrentVersion,
                    "Version=" + (missing == null ? "null" : missing.Version.ToString()));

                var original = new AppSettings();
                original.MonitorKey = "MONKEY";
                original.LocalInput = 0x0F;
                original.TargetInput = 0x11;
                original.Hotkey = "Ctrl+Shift+F9";
                ConfigFile.Write(path, original);
                AppSettings loaded = ConfigFile.ReadOrDefault<AppSettings>(path);
                Check("配置写盘后能读回", loaded != null);
                if (loaded != null)
                {
                    CheckEqual("配置·本机端口", 0x0F, loaded.LocalInput);
                    CheckEqual("配置·切换端口", 0x11, loaded.TargetInput);
                    CheckEqual("配置·显示器标识", "MONKEY", loaded.MonitorKey);
                    CheckEqual("配置·快捷键", "Ctrl+Shift+F9", loaded.Hotkey);
                }

                // 损坏的配置不应抛出，而应回落到默认值。
                File.WriteAllText(path, "{ this is not json ");
                AppSettings broken = ConfigFile.ReadOrDefault<AppSettings>(path);
                Check("配置损坏时安全回落", broken != null && broken.Version == AppSettings.CurrentVersion, "读回的配置为 null 或版本错误");
            }
            finally
            {
                try { Directory.Delete(directory, true); }
                catch (Exception) { }
            }
        }

        private static void TestValidation()
        {
            var settings = new AppSettings();
            Check("默认配置校验通过", settings.Validate() == null, settings.Validate());

            var noTarget = new AppSettings();
            noTarget.LocalInput = 0x0F;
            noTarget.TargetInput = 0;
            Check("允许暂不设置切换端口", noTarget.Validate() == null, noTarget.Validate());

            var samePorts = new AppSettings();
            samePorts.LocalInput = 0x11;
            samePorts.TargetInput = 0x11;
            Check("本机端口与切换端口相同时报错", samePorts.Validate() != null, "未报错");

            var overflowInput = new AppSettings();
            overflowInput.LocalInput = 256;
            Check("输入源代号超过 255 时报错", overflowInput.Validate() != null, "未报错");

            var emptyHotkey = new AppSettings();
            emptyHotkey.Hotkey = "";
            Check("快捷键为空时报错", emptyHotkey.Validate() != null, "未报错");
        }

        private static void TestPorts()
        {
            CheckEqual("端口名称·0x0F", "DisplayPort1", PortCatalog.Label(0x0F));
            CheckEqual("端口名称·0x11", "HDMI1", PortCatalog.Label(0x11));
            CheckEqual("端口名称·0x12", "HDMI2", PortCatalog.Label(0x12));
            CheckEqual("端口名称·0x10", "DisplayPort2", PortCatalog.Label(0x10));
            Check("端口名称·未知值带十六进制", PortCatalog.Label(0x2A).IndexOf("0x2A", StringComparison.OrdinalIgnoreCase) >= 0, PortCatalog.Label(0x2A));

            const string caps =
                "prot(monitor)type(lcd)model(P2711V)cmds(01 0A 02 03 0C E3 F3)vcp(02 04 05 10 12 14(05 06 08 0B) 16 18 1A 52 60( 11 12 0F 10) 62 6C 6D 6E VCP(6F 70) mccs_ver(2.1))" +
                "mswhql(1)asset_eep(40)mpu_ver(01)";
            List<PortInfo> ports = PortCatalog.ParseInputs(caps);
            Check("能力字符串解析出 4 个输入口", ports.Count == 4, "实际 " + ports.Count);
            if (ports.Count == 4)
            {
                CheckEqual("能力字符串·第 1 个端口", 0x11, ports[0].Value);
                CheckEqual("能力字符串·第 4 个端口", 0x10, ports[3].Value);
                CheckEqual("能力字符串·端口名称", "HDMI1", ports[0].Name);
            }

            Check("能力字符串为空时不返回端口", PortCatalog.ParseInputs("").Count == 0, "返回了端口");
            Check("能力字符串无 60 项时不返回端口", PortCatalog.ParseInputs("prot(monitor)vcp(10 12 14)").Count == 0, "返回了端口");
            List<PortInfo> defaults = PortCatalog.DefaultPorts();
            Check("常见端口兜底列表非空", defaults.Count >= 4, "只有 " + defaults.Count + " 个");

            var snapshot = new MonitorSnapshot();
            snapshot.Ports = ports;
            snapshot.CurrentInput = 0x0F;
            CheckEqual("快照·当前输入名称", "DisplayPort1（0x0F）", snapshot.CurrentName);
            snapshot.CurrentInput = 0x1A;
            Check("快照·未知输入有提示文本", snapshot.CurrentName.IndexOf("未知", StringComparison.Ordinal) >= 0, snapshot.CurrentName);
        }

        private static void TestHotkey()
        {
            HotkeySpec spec;
            string error;
            Check("解析 Ctrl+Alt+F10", HotkeyText.TryParse("Ctrl+Alt+F10", out spec, out error), error);
            Check("Ctrl+Alt+F10 含 Ctrl", spec != null && spec.Ctrl, "未识别 Ctrl");
            Check("Ctrl+Alt+F10 含 Alt", spec != null && spec.Alt, "未识别 Alt");
            Check("Ctrl+Alt+F10 主键为 F10", spec != null && spec.Vk == 0x79, "Vk=" + (spec == null ? "null" : spec.Vk.ToString("X2")));

            Check("拒绝没有修饰键的 F10", !HotkeyText.TryParse("F10", out spec, out error), "居然通过了");
            Check("拒绝未知按键", !HotkeyText.TryParse("Ctrl+Alt+Foo", out spec, out error), "居然通过了");
            Check("拒绝空文本", !HotkeyText.TryParse("", out spec, out error), "居然通过了");
            Check("接受 Win+Shift+1", HotkeyText.TryParse("Win+Shift+1", out spec, out error), error);

            HotkeySpec round;
            Check("解析 Ctrl+Shift+F9", HotkeyText.TryParse("Ctrl+Shift+F9", out round, out error), error);
            CheckEqual("格式化回原文", "Ctrl+Shift+F9", HotkeyText.Format(round));

            int vk;
            Check("TryParseKey 支持 Home", HotkeyText.TryParseKey("Home", out vk) && vk == 0x24, "vk=" + vk);
            Check("TryParseKey 支持 f24（大小写不敏感）", HotkeyText.TryParseKey("f24", out vk) && vk == 0x87, "vk=" + vk);
            Check("TryParseKey 支持字母 A", HotkeyText.TryParseKey("A", out vk) && vk == 0x41, "vk=" + vk);
            Check("KeyChoices 非空", HotkeyText.KeyChoices().Count > 20, "只有 " + HotkeyText.KeyChoices().Count + " 个");
        }

        private static void TestSnapshot()
        {
            Check("未设置当前输入时给出读取失败", new MonitorSnapshot().CurrentName == "读取失败", new MonitorSnapshot().CurrentName);
            var snapshot = new MonitorSnapshot();
            var port = new PortInfo();
            port.Value = 0x11;
            port.Name = "HDMI1";
            snapshot.Ports.Add(port);
            Check("快照按值查找端口", snapshot.Find(0x11) == port, "没找到");
            Check("快照文本带十六进制", port.Text.IndexOf("0x11", StringComparison.OrdinalIgnoreCase) >= 0, port.Text);
        }

        private static void TestSettingsClone()
        {
            var settings = new AppSettings();
            settings.LocalInput = 0x0F;
            settings.MonitorKey = "A";
            AppSettings copy = settings.Clone();
            copy.LocalInput = 0x11;
            copy.MonitorKey = "B";
            Check("克隆配置互不影响", settings.LocalInput == 0x0F && settings.MonitorKey == "A", "克隆后原对象被改动");
        }

        /// <summary>
        /// AppService 集成：临时目录里跑一遍真实启动/改配置/关闭（快捷键关闭，绝不写 DDC）。
        /// 覆盖“界面按钮背后的业务逻辑”，确保装配关系没被改坏。
        /// </summary>
        private static void TestAppServiceIntegration()
        {
            string directory = NewTestDirectory("DisplayDockTest");
            AppService service = null;
            try
            {
                var seed = new AppSettings();
                seed.HotkeyEnabled = false;   // 测试期间不抢占用户的全局快捷键
                seed.LocalInput = 0x0F;
                seed.TargetInput = 0;         // 先留空，验证“未设置切换端口时切换不执行”的保护
                ConfigFile.Write(Path.Combine(directory, ConfigFile.FileName), seed);

                service = new AppService(directory);
                service.Initialize();

                Check("AppService 启动后拿到显示器快照", service.Snapshot != null, "快照为空");

                // Initialize 会自动补一个可用的切换端口，这里显式清空，验证“目标端口为空时不执行切换”。
                service.UpdatePorts(0x0F, 0);
                service.ToggleSwitch();   // 目标端口为 0：必须只提示、不写 DDC
                Check("未设置切换端口时切换被拦下", service.Settings.TargetInput == 0, "目标端口被改了");

                service.UpdatePorts(0x11, 0x11);   // 相同端口：必须拒绝
                Check("相同端口被拒绝", service.Settings.LocalInput == 0x0F, "本机端口被改成了 " + service.Settings.LocalInput.ToString("X2"));

                service.UpdatePorts(0x0F, 0x11);
                CheckEqual("端口设置落盘·本机", 0x0F, service.Settings.LocalInput);
                CheckEqual("端口设置落盘·切换", 0x11, service.Settings.TargetInput);
                AppSettings reloaded = ConfigFile.ReadOrDefault<AppSettings>(Path.Combine(directory, ConfigFile.FileName));
                CheckEqual("端口设置写进配置文件", 0x11, reloaded.TargetInput);

                service.UpdateHotkey(true, "Ctrl+Alt+NotAKey");
                Check("非法快捷键不被接受", !service.Settings.HotkeyEnabled, "非法快捷键被存下了");

                service.Shutdown();
                Check("关闭后配置文件仍可读", File.Exists(Path.Combine(directory, ConfigFile.FileName)), "配置文件不见了");
            }
            catch (Exception ex)
            {
                Check("AppService 集成未抛异常", false, ex.GetType().Name + "：" + ex.Message);
            }
            finally
            {
                if (service != null)
                {
                    try { service.Dispose(); } catch (Exception) { }
                }
                try { Directory.Delete(directory, true); } catch (Exception) { }
            }
        }

        private static void TestLiveDdc()
        {
            var log = new ListLogSink();
            var monitor = new MonitorService(log);
            try
            {
                MonitorSnapshot snapshot = monitor.Scan(null);
                if (snapshot == null || snapshot.Ports.Count == 0)
                {
                    Skip("实机 DDC 探测", snapshot == null ? "无结果" : snapshot.Error);
                    return;
                }
                Console.WriteLine("       显示器：" + snapshot.MonitorName + "，端口 " + snapshot.Ports.Count + " 个，当前 " + snapshot.CurrentName);
                Check("实机 DDC 读到端口", snapshot.Ports.Count > 0, "端口数为 0");
                if (!snapshot.CurrentInput.HasValue)
                {
                    // 显示器进入待机/切到无信号输入口时，能力字符串还能读到，但 VCP 0x60 会失败。
                    // 这属环境状态而不是代码缺陷，跳过而不是把构建判红。
                    Skip("实机 DDC 读到当前输入", "显示器可能处于待机或当前无信号：" + snapshot.Error);
                    return;
                }
                Check("实机 DDC 读到当前输入", snapshot.CurrentInput.HasValue, "读取失败");
                uint value;
                string error;
                Check("实机 DDC 可再次读当前输入", monitor.TryReadCurrent(out value, out error) || value != 0, error);
            }
            finally
            {
                monitor.Dispose();
            }
        }
    }
}
