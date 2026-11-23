using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace DisplayDock
{
    /// <summary>
    /// DDC/CI 显示器会话：枚举显示器、读 MCCS 能力字符串、读/写输入源（VCP 0x60）。
    /// 每次切换后显示器会从本机“消失”，所以任何读写失败都会自动重建会话再试一次。
    /// 所有公开方法都不抛出。
    /// </summary>
    public sealed class MonitorService : IMonitorService
    {
        private readonly object gate = new object();
        private readonly ILogSink log;
        private NativeSession session;
        private MonitorSnapshot snapshot;
        private bool disposed;

        public MonitorService(ILogSink log)
        {
            this.log = log;
        }

        public MonitorSnapshot Snapshot
        {
            get { lock (gate) { return snapshot; } }
        }

        public MonitorSnapshot Scan(string preferredMonitorKey)
        {
            lock (gate)
            {
                if (disposed) return BuildEmptySnapshot("程序正在退出。");
                try
                {
                    CloseSession();
                    session = new NativeSession();
                    if (session.Displays.Count == 0)
                        return BuildEmptySnapshot("未检测到显示器。请确认视频线已连接、显示器已开机。");

                    DisplayMonitor chosen = ChooseDisplay(preferredMonitorKey);
                    if (chosen == null)
                        return BuildEmptySnapshot("找不到唯一可识别的显示器。请使用“扩展显示”模式或换一根支持 DDC/CI 的视频线后重试。");

                    var result = new MonitorSnapshot();
                    result.MonitorName = chosen.Name;
                    result.MonitorKey = chosen.Key;
                    result.Ready = chosen.Selectable && chosen.Handle != IntPtr.Zero || chosen.Selectable;

                    string capError;
                    string caps = session.ReadCapabilities(chosen, out capError);
                    result.Capabilities = caps;
                    List<PortInfo> ports = PortCatalog.ParseInputs(caps);
                    if (ports.Count > 0)
                    {
                        result.Ports = ports;
                    }
                    else
                    {
                        result.Ports = PortCatalog.DefaultPorts();
                        result.Error = "未能从显示器读取端口列表（DDC/CI 可能未开启）。已列出常见端口，请手动确认。" +
                                       (String.IsNullOrEmpty(capError) ? "" : " 详情：" + capError);
                    }

                    uint value;
                    string readError;
                    if (session.ReadInput(chosen, out value, out readError))
                    {
                        result.CurrentInput = value;
                    }
                    else
                    {
                        result.Error = AppendError(result.Error, "读取当前输入源失败：" + readError);
                        result.Ready = false;
                    }

                    snapshot = result;
                    if (log != null)
                    {
                        log.Write("显示器探测： " + result.MonitorName + "  端口 " + result.Ports.Count + " 个  当前 " +
                            (result.CurrentInput.HasValue ? ("0x" + result.CurrentInput.Value.ToString("X2") + " " + PortCatalog.Label(result.CurrentInput.Value)) : "读取失败"));
                    }
                    return result;
                }
                catch (Exception ex)
                {
                    CloseSession();
                    return BuildEmptySnapshot("显示器探测失败：" + ex.Message);
                }
            }
        }

        public bool TryReadCurrent(out uint value, out string error)
        {
            lock (gate)
            {
                value = 0;
                error = null;
                if (disposed) { error = "程序正在退出。"; return false; }
                if (session == null && Scan(null).Ports.Count == 0) { error = snapshot == null ? "尚未探测显示器。" : snapshot.Error; return false; }
                DisplayMonitor display = ReadyDisplay(out error);
                if (display == null) return false;
                string readError;
                if (session.ReadInput(display, out value, out readError)) return true;
                // 会话可能已失效（切换后显示器断链）：重建后重试一次。
                error = readError;
                RefreshSession();
                display = ReadyDisplay(out error);
                if (display == null) return false;
                if (session.ReadInput(display, out value, out readError)) return true;
                error = readError;
                return false;
            }
        }

        public bool TryWriteInput(uint value, out string error)
        {
            lock (gate)
            {
                error = null;
                if (disposed) { error = "程序正在退出。"; return false; }
                if (value < 1 || value > 255) { error = "输入源代号必须在 1 至 255 之间。"; return false; }
                if (session == null) Scan(null);
                DisplayMonitor display = ReadyDisplay(out error);
                if (display == null) return false;
                if (session.WriteInput(display, value))
                {
                    // 切换后本机可能立刻失去 DDC 通道：主动释放句柄，下次操作重新枚举。
                    CloseSession();
                    snapshot = null;
                    return true;
                }
                error = "写入输入源指令失败。";
                RefreshSession();
                display = ReadyDisplay(out error);
                if (display == null) return false;
                if (session.WriteInput(display, value))
                {
                    CloseSession();
                    snapshot = null;
                    return true;
                }
                error = "写入输入源指令失败。请确认显示器菜单中的 DDC/CI 已开启。";
                return false;
            }
        }

        private void RefreshSession()
        {
            try
            {
                CloseSession();
                session = new NativeSession();
            }
            catch (Exception ex)
            {
                session = null;
                if (log != null) log.Write("重建显示器会话失败：" + ex.Message);
            }
        }

        private DisplayMonitor ReadyDisplay(out string error)
        {
            error = null;
            if (session == null)
            {
                error = "显示器会话不可用，请先刷新。";
                return null;
            }
            DisplayMonitor chosen = ChooseDisplay(snapshot == null ? "" : snapshot.MonitorKey);
            if (chosen == null)
            {
                error = "找不到唯一可识别的显示器，请刷新后重试。";
                return null;
            }
            return chosen;
        }

        private DisplayMonitor ChooseDisplay(string preferredMonitorKey)
        {
            if (session == null) return null;
            if (!String.IsNullOrWhiteSpace(preferredMonitorKey))
            {
                foreach (DisplayMonitor display in session.Displays)
                {
                    if (display.Selectable && String.Equals(display.Key, preferredMonitorKey, StringComparison.OrdinalIgnoreCase))
                        return display;
                }
            }
            DisplayMonitor unique = null;
            int count = 0;
            foreach (DisplayMonitor display in session.Displays)
            {
                if (!display.Selectable) continue;
                unique = display;
                count++;
            }
            return count == 1 ? unique : null;
        }

        private MonitorSnapshot BuildEmptySnapshot(string error)
        {
            var result = new MonitorSnapshot();
            result.Error = error;
            result.Ready = false;
            snapshot = result;
            if (log != null) log.Write("显示器探测失败：" + error);
            return result;
        }

        private static string AppendError(string existing, string addition)
        {
            return String.IsNullOrEmpty(existing) ? addition : existing + " " + addition;
        }

        private void CloseSession()
        {
            if (session == null) return;
            try { session.Dispose(); }
            catch (Exception) { }
            session = null;
        }

        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;
                CloseSession();
            }
        }
    }

    /// <summary>一台逻辑显示器（一个 HMONITOR 下的物理显示器）。</summary>
    internal sealed class DisplayMonitor
    {
        public string Key = "";
        public string Name = "";
        public string Device = "";
        public bool Selectable;
        public string Note = "";
        public IntPtr Handle;

        public override string ToString() { return Name + "  ·  " + Device; }
    }

    /// <summary>一次 DDC 会话：持有物理显示器句柄，Dispose 时统一释放。</summary>
    internal sealed class NativeSession : IDisposable
    {
        private readonly List<DisplayMonitor> displays = new List<DisplayMonitor>();
        private readonly List<IntPtr> owned = new List<IntPtr>();
        private bool disposed;

        public IList<DisplayMonitor> Displays { get { return displays.AsReadOnly(); } }

        public NativeSession()
        {
            Exception callbackError = null;
            Native.MonitorEnum callback = delegate(IntPtr monitor, IntPtr dc, ref Native.Rect rect, IntPtr data)
            {
                try { AddMonitor(monitor); return true; }
                catch (Exception ex) { callbackError = ex; return false; }
            };
            try
            {
                bool ok = Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
                if (callbackError != null) throw callbackError;
                if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法枚举当前会话中的显示器。");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void AddMonitor(IntPtr monitor)
        {
            var info = new Native.MonitorInfo();
            info.Size = Marshal.SizeOf(typeof(Native.MonitorInfo));
            if (!Native.GetMonitorInfo(monitor, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
            var identities = new List<string>();
            string friendly = "显示器";
            for (uint index = 0; ; index++)
            {
                var dev = new Native.DisplayDevice();
                dev.Size = Marshal.SizeOf(typeof(Native.DisplayDevice));
                if (!Native.EnumDisplayDevices(info.Device, index, ref dev, 1)) break;
                if ((dev.StateFlags & 1) == 0) continue;
                if (!String.IsNullOrWhiteSpace(dev.DeviceID)) identities.Add(dev.DeviceID);
                if (!String.IsNullOrWhiteSpace(dev.DeviceString)) friendly = dev.DeviceString;
            }
            uint count;
            if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out count) || count == 0)
            {
                displays.Add(new DisplayMonitor { Key = "", Name = friendly, Device = info.Device, Selectable = false,
                    Note = "未取得物理显示器句柄。可能是内置屏、虚拟屏或当前驱动不提供 DDC/CI。" });
                return;
            }
            if (count > 64) throw new InvalidOperationException("驱动报告的物理显示器数量异常。");
            var physical = new Native.PhysicalMonitor[count];
            if (!Native.GetPhysicalMonitorsFromHMONITOR(monitor, count, physical))
            {
                displays.Add(new DisplayMonitor { Key = "", Name = friendly, Device = info.Device, Selectable = false,
                    Note = "无法取得物理显示器。Win32 错误码：" + Marshal.GetLastWin32Error() });
                return;
            }
            foreach (var item in physical) owned.Add(item.Handle);
            bool unique = count == 1 && identities.Count == 1;
            string edidName = unique ? ReadEdidName(identities[0]) : null;
            for (int i = 0; i < physical.Length; i++)
            {
                displays.Add(new DisplayMonitor
                {
                    Key = unique ? identities[0] : "",
                    Device = info.Device,
                    Name = edidName != null ? edidName : (String.IsNullOrWhiteSpace(physical[i].Description) ? friendly : physical[i].Description),
                    Handle = physical[i].Handle,
                    Selectable = unique,
                    Note = unique ? "已取得物理句柄。" : "此逻辑屏幕关联多个或无法唯一识别的设备。请改用扩展显示模式后重试。"
                });
            }
        }

        private static string ReadEdidName(string interfaceName)
        {
            try
            {
                string[] parts = interfaceName.Split('#');
                if (parts.Length < 3) return null;
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + parts[1] + "\\" + parts[2] + @"\Device Parameters"))
                {
                    byte[] edid = key == null ? null : key.GetValue("EDID") as byte[];
                    if (edid == null || edid.Length < 128) return null;
                    for (int offset = 54; offset <= 108; offset += 18)
                    {
                        if (edid[offset] == 0 && edid[offset + 1] == 0 && edid[offset + 2] == 0 && edid[offset + 3] == 0xFC)
                        {
                            string name = Encoding.ASCII.GetString(edid, offset + 5, 13).Trim('\0', '\r', '\n', ' ');
                            if (name.Length > 0) return name;
                        }
                    }
                }
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.IO.IOException) { }
            return null;
        }

        private void Check(DisplayMonitor display)
        {
            if (disposed) throw new ObjectDisposedException("NativeSession", "显示器会话已释放，请刷新后重试。");
            if (display == null || !displays.Contains(display))
                throw new InvalidOperationException("显示器不属于当前枚举会话，请刷新后重试。");
            if (!display.Selectable) throw new InvalidOperationException("显示器无法唯一识别，请重新选择显示器。");
            if (!owned.Contains(display.Handle))
                throw new InvalidOperationException("当前会话未持有该显示器的物理句柄，请刷新后重试。");
        }

        private static string NativeFailure(string operation, int code)
        {
            return operation + "。Win32 错误码：" + code + "（0x" + unchecked((uint)code).ToString("X8") + "）。" + new Win32Exception(code).Message;
        }

        public bool ReadInput(DisplayMonitor display, out uint value, out string error)
        {
            value = 0;
            error = null;
            try
            {
                Check(display);
                uint max;
                bool ok = Native.GetVCPFeatureAndVCPFeatureReply(display.Handle, 0x60, IntPtr.Zero, out value, out max);
                int code = ok ? 0 : Marshal.GetLastWin32Error();
                error = ok ? null : NativeFailure("读取输入源失败", code);
                return ok;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public string ReadCapabilities(DisplayMonitor display, out string error)
        {
            error = null;
            try
            {
                Check(display);
                uint length;
                if (!Native.GetCapabilitiesStringLength(display.Handle, out length))
                {
                    error = NativeFailure("读取能力字符串长度失败", Marshal.GetLastWin32Error());
                    return null;
                }
                if (length == 0 || length > 65536) { error = "能力字符串长度无效。"; return null; }
                var buffer = new StringBuilder((int)length);
                if (!Native.CapabilitiesRequestAndCapabilitiesReply(display.Handle, buffer, length))
                {
                    error = NativeFailure("读取能力字符串失败", Marshal.GetLastWin32Error());
                    return null;
                }
                return buffer.ToString();
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        public bool WriteInput(DisplayMonitor display, uint value)
        {
            Check(display);
            if (value < 1 || value > 255) throw new ArgumentOutOfRangeException("value");
            if (!Native.SetVCPFeature(display.Handle, 0x60, value))
                throw new InvalidOperationException(NativeFailure("系统未确认输入源指令", Marshal.GetLastWin32Error()) +
                    "。请观察屏幕；指令可能已生效，程序不会重复发送。若画面未变，请确认显示器菜单中的 DDC/CI 已开启。");
            return true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (IntPtr handle in owned)
            {
                try { Native.DestroyPhysicalMonitor(handle); }
                catch (Exception) { }
            }
            owned.Clear();
            foreach (DisplayMonitor display in displays) display.Handle = IntPtr.Zero;
        }
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct MonitorInfo
        {
            public int Size;
            public Rect Monitor, Work;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct DisplayDevice
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct PhysicalMonitor
        {
            public IntPtr Handle;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        }

        internal delegate bool MonitorEnum(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnum callback, IntPtr data);

        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice info, uint flags);

        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

        [DllImport("dxva2.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitor[] physical);

        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool DestroyPhysicalMonitor(IntPtr monitor);

        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte code, IntPtr type, out uint current, out uint maximum);

        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool SetVCPFeature(IntPtr monitor, byte code, uint value);

        [DllImport("dxva2.dll", SetLastError = true)]
        internal static extern bool GetCapabilitiesStringLength(IntPtr monitor, out uint length);

        [DllImport("dxva2.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        internal static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr monitor, StringBuilder text, uint length);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);
    }
}
