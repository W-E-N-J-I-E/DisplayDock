using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace DisplayDock
{
    /// <summary>
    /// 全程序唯一的持久化配置。
    /// 兼容性：只增字段，不改字段类型；反序列化缺字段时保留默认值。
    /// 只保存“显示器 + 输入口 + 快捷键”。
    /// </summary>
    [DataContract]
    public sealed class AppSettings
    {
        /// <summary>当前配置版本。</summary>
        public const int CurrentVersion = 1;

        [DataMember(Order = 0)] public int Version = CurrentVersion;
        /// <summary>显示器的唯一标识（Display.Key），空=自动取唯一可识别的显示器。</summary>
        [DataMember(Order = 1)] public string MonitorKey = "";
        /// <summary>本机视频口在 VCP 0x60 下的值（例如 0x0F=DisplayPort1）。0=未设置。</summary>
        [DataMember(Order = 2)] public uint LocalInput;
        /// <summary>用户选择的“切换屏幕端口”（VCP 0x60 值）。</summary>
        [DataMember(Order = 3)] public uint TargetInput;
        /// <summary>切换快捷键文本，例如 Ctrl+Alt+F10。</summary>
        [DataMember(Order = 4)] public string Hotkey = "Ctrl+Alt+F10";
        [DataMember(Order = 5)] public bool HotkeyEnabled = true;

        public AppSettings Clone()
        {
            var copy = new AppSettings();
            copy.Version = Version;
            copy.MonitorKey = MonitorKey;
            copy.LocalInput = LocalInput;
            copy.TargetInput = TargetInput;
            copy.Hotkey = Hotkey;
            copy.HotkeyEnabled = HotkeyEnabled;
            return copy;
        }

        /// <summary>返回错误文本；null 表示通过。</summary>
        public string Validate()
        {
            if (LocalInput > 255 || TargetInput > 255)
                return "输入源代号必须在 1 至 255 之间。";
            if (TargetInput != 0 && TargetInput == LocalInput)
                return "“切换屏幕端口”不能和“本机端口”相同。";
            if (String.IsNullOrWhiteSpace(Hotkey))
                return "快捷键不能为空。";
            return null;
        }
    }

    /// <summary>配置文件读写：UTF-8、原子替换、损坏时返回默认值而不抛出。</summary>
    public static class ConfigFile
    {
        /// <summary>本程序的配置文件。</summary>
        public const string FileName = "displaydock.json";

        public static T ReadOrDefault<T>(string path) where T : class, new()
        {
            try
            {
                if (!File.Exists(path)) return new T();
                using (var stream = File.OpenRead(path))
                    return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
            }
            catch (Exception)
            {
                return new T();
            }
        }

        public static string Encode<T>(T item)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, item);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static void Write<T>(string path, T item)
        {
            path = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, Encode(item), new UTF8Encoding(false));
                if (!File.Exists(path))
                {
                    File.Move(temporary, path);
                }
                else
                {
                    try
                    {
                        File.Replace(temporary, path, null);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // 个别文件系统/安全软件会拦截 ReplaceFile；退化为删除后改名，内容仍是完整写入的。
                        File.Delete(path);
                        File.Move(temporary, path);
                    }
                    catch (IOException)
                    {
                        File.Delete(path);
                        File.Move(temporary, path);
                    }
                }
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    /// <summary>显示器的一个视频输入口。</summary>
    [DataContract]
    public sealed class PortInfo
    {
        /// <summary>VCP 0x60 取值（MCCS 输入源代号）。</summary>
        [DataMember(Order = 0)] public uint Value;
        /// <summary>友好名称，如 DisplayPort1 / HDMI2 / USB-C。</summary>
        [DataMember(Order = 1)] public string Name = "";

        public string Text
        {
            get
            {
                string name = String.IsNullOrEmpty(Name) ? "未知输入" : Name;
                return name + "（0x" + Value.ToString("X2") + "）";
            }
        }

        public override string ToString() { return Text; }
    }

    /// <summary>一次显示器能力探测的结果。</summary>
    public sealed class MonitorSnapshot
    {
        public string MonitorName = "";
        public string MonitorKey = "";
        /// <summary>显示器当前正在显示的输入口值；读不到为 null。</summary>
        public uint? CurrentInput;
        public List<PortInfo> Ports = new List<PortInfo>();
        public string Capabilities = "";
        /// <summary>探测错误（DDC/CI 不可用、无显示器等）。</summary>
        public string Error = "";
        /// <summary>显示器句柄是否可用（能读写 DDC）。</summary>
        public bool Ready;

        public PortInfo Find(uint value)
        {
            return Ports.FirstOrDefault(p => p.Value == value);
        }

        public string CurrentName
        {
            get
            {
                if (!CurrentInput.HasValue) return "读取失败";
                PortInfo port = Find(CurrentInput.Value);
                return port == null ? ("未知输入（0x" + CurrentInput.Value.ToString("X2") + "）") : port.Text;
            }
        }
    }

    /// <summary>DDC/CI 会话：枚举显示器、读能力、读/写输入源。</summary>
    public interface IMonitorService : IDisposable
    {
        /// <summary>最近一次探测结果的快照（可能为 null）。</summary>
        MonitorSnapshot Snapshot { get; }
        /// <summary>重新枚举显示器 → 读能力字符串 → 解析端口列表 → 读当前输入。不抛出。</summary>
        MonitorSnapshot Scan(string preferredMonitorKey);
        /// <summary>读当前输入口。会话失效时先自动重新扫描一次。</summary>
        bool TryReadCurrent(out uint value, out string error);
        /// <summary>写输入口（提交切换指令）。</summary>
        bool TryWriteInput(uint value, out string error);
    }

    /// <summary>日志出口（界面日志框）。</summary>
    public interface ILogSink
    {
        void Write(string line);
    }

    public sealed class ListLogSink : ILogSink
    {
        private readonly List<string> lines = new List<string>();

        public void Write(string line)
        {
            lock (lines)
            {
                lines.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + line);
                while (lines.Count > 400) lines.RemoveAt(0);
            }
        }

        public string[] Snapshot()
        {
            lock (lines) { return lines.ToArray(); }
        }
    }
}
