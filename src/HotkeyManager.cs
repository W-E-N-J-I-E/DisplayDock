using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace DisplayDock
{
    /// <summary>一条快捷键：修饰键组合 + 主键虚拟码。</summary>
    public sealed class HotkeySpec
    {
        public bool Ctrl;
        public bool Alt;
        public bool Shift;
        public bool Win;
        /// <summary>虚拟键码（Win32 VK_*），例如 F10 = 0x79。</summary>
        public int Vk;
    }

    /// <summary>快捷键文本 ↔ HotkeySpec 的解析、格式化与可选键列表。</summary>
    public static class HotkeyText
    {
        public const int ModAlt = 0x0001;
        public const int ModControl = 0x0002;
        public const int ModShift = 0x0004;
        public const int ModWin = 0x0008;
        public const int ModNoRepeat = 0x4000;

        private static readonly Dictionary<int, string> KeyNames = BuildKeyNames();

        private static Dictionary<int, string> BuildKeyNames()
        {
            var names = new Dictionary<int, string>();
            for (int i = 1; i <= 24; i++) names[0x6F + i] = "F" + i;          // F1..F24
            for (char c = 'A'; c <= 'Z'; c++) names[c] = c.ToString();
            for (char c = '0'; c <= '9'; c++) names[c] = c.ToString();
            names[0x24] = "Home";
            names[0x23] = "End";
            names[0x21] = "PageUp";
            names[0x22] = "PageDown";
            names[0x2D] = "Insert";
            names[0x2E] = "Delete";
            names[0x2C] = "PrintScreen";
            names[0x13] = "Pause";
            names[0x91] = "ScrollLock";
            names[0x20] = "Space";
            names[0x25] = "Left";
            names[0x26] = "Up";
            names[0x27] = "Right";
            names[0x28] = "Down";
            return names;
        }

        /// <summary>可选的主键列表（界面下拉框用）。</summary>
        public static List<KeyValuePair<int, string>> KeyChoices()
        {
            var list = new List<KeyValuePair<int, string>>();
            for (int i = 1; i <= 24; i++) list.Add(new KeyValuePair<int, string>(0x6F + i, "F" + i));
            foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ") list.Add(new KeyValuePair<int, string>(c, c.ToString()));
            foreach (char c in "0123456789") list.Add(new KeyValuePair<int, string>(c, c.ToString()));
            list.Add(new KeyValuePair<int, string>(0x24, "Home"));
            list.Add(new KeyValuePair<int, string>(0x23, "End"));
            list.Add(new KeyValuePair<int, string>(0x21, "PageUp"));
            list.Add(new KeyValuePair<int, string>(0x22, "PageDown"));
            list.Add(new KeyValuePair<int, string>(0x2D, "Insert"));
            list.Add(new KeyValuePair<int, string>(0x2E, "Delete"));
            list.Add(new KeyValuePair<int, string>(0x2C, "PrintScreen"));
            return list;
        }

        public static string Format(HotkeySpec spec)
        {
            if (spec == null) return "";
            var text = new StringBuilder();
            if (spec.Ctrl) text.Append("Ctrl+");
            if (spec.Alt) text.Append("Alt+");
            if (spec.Shift) text.Append("Shift+");
            if (spec.Win) text.Append("Win+");
            string key;
            if (!KeyNames.TryGetValue(spec.Vk, out key)) key = "0x" + spec.Vk.ToString("X2");
            text.Append(key);
            return text.ToString();
        }

        public static bool TryParse(string text, out HotkeySpec spec, out string error)
        {
            spec = null;
            error = null;
            if (String.IsNullOrWhiteSpace(text))
            {
                error = "快捷键不能为空。";
                return false;
            }
            string[] parts = text.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                error = "快捷键格式不正确。";
                return false;
            }
            var result = new HotkeySpec();
            bool hasKey = false;
            foreach (string rawPart in parts)
            {
                string part = rawPart.Trim();
                if (part.Length == 0) continue;
                string upper = part.ToUpperInvariant();
                if (upper == "CTRL" || upper == "CONTROL") { result.Ctrl = true; continue; }
                if (upper == "ALT") { result.Alt = true; continue; }
                if (upper == "SHIFT") { result.Shift = true; continue; }
                if (upper == "WIN" || upper == "WINDOWS") { result.Win = true; continue; }
                int vk;
                if (!TryParseKey(part, out vk))
                {
                    error = "无法识别的按键：“" + part + "”。";
                    return false;
                }
                if (hasKey)
                {
                    error = "快捷键只能有一个主键。";
                    return false;
                }
                result.Vk = vk;
                hasKey = true;
            }
            if (!hasKey || result.Vk == 0)
            {
                error = "快捷键缺少主键（例如 F10 或 A）。";
                return false;
            }
            if (!result.Ctrl && !result.Alt && !result.Shift && !result.Win)
            {
                error = "快捷键至少需要一个 Ctrl、Alt、Shift 或 Win 修饰键，否则会抢占正常输入。";
                return false;
            }
            spec = result;
            return true;
        }

        public static bool TryParseKey(string text, out int vk)
        {
            vk = 0;
            if (String.IsNullOrWhiteSpace(text)) return false;
            string upper = text.Trim().ToUpperInvariant();
            if (upper.Length == 0) return false;
            if (upper[0] == 'F')
            {
                int number;
                if (upper.Length > 1 && Int32.TryParse(upper.Substring(1), out number) && number >= 1 && number <= 24)
                {
                    vk = 0x6F + number;
                    return true;
                }
            }
            if (upper.Length == 1 && ((upper[0] >= 'A' && upper[0] <= 'Z') || (upper[0] >= '0' && upper[0] <= '9')))
            {
                vk = (int)upper[0];
                return true;
            }
            switch (upper)
            {
                case "HOME": vk = 0x24; return true;
                case "END": vk = 0x23; return true;
                case "PAGEUP": vk = 0x21; return true;
                case "PAGEDOWN": vk = 0x22; return true;
                case "INSERT": vk = 0x2D; return true;
                case "DELETE": vk = 0x2E; return true;
                case "PRINTSCREEN": vk = 0x2C; return true;
                case "PAUSE": vk = 0x13; return true;
                case "SCROLLLOCK": vk = 0x91; return true;
                case "SPACE": vk = 0x20; return true;
                case "LEFT": vk = 0x25; return true;
                case "UP": vk = 0x26; return true;
                case "RIGHT": vk = 0x27; return true;
                case "DOWN": vk = 0x28; return true;
            }
            if (upper.StartsWith("0X", StringComparison.Ordinal))
            {
                int parsed;
                if (Int32.TryParse(upper.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out parsed) && parsed > 0)
                {
                    vk = parsed;
                    return true;
                }
            }
            return false;
        }

        public static uint Modifiers(HotkeySpec spec)
        {
            uint modifiers = 0;
            if (spec.Alt) modifiers |= ModAlt;
            if (spec.Ctrl) modifiers |= ModControl;
            if (spec.Shift) modifiers |= ModShift;
            if (spec.Win) modifiers |= ModWin;
            return modifiers | ModNoRepeat;
        }
    }

    /// <summary>基于隐藏窗口的全局快捷键注册器（WM_HOTKEY 必须绑到窗口）。</summary>
    public sealed class HotkeyRegistrar : IDisposable
    {
        private const int WmHotkey = 0x0312;
        private const int HotkeyId = 0x4C4B;

        private sealed class MessageWindow : NativeWindow
        {
            public Action<int> Hotkey;
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
                {
                    Action<int> handler = Hotkey;
                    if (handler != null) handler(m.WParam.ToInt32());
                    return;
                }
                base.WndProc(ref m);
            }
        }

        private MessageWindow window;
        private bool registered;
        private readonly ILogSink log;

        public HotkeyRegistrar(ILogSink log)
        {
            this.log = log;
            window = new MessageWindow();
            var parameters = new CreateParams();
            parameters.Caption = "DisplayDockHotkey";
            parameters.Parent = new IntPtr(-3); // HWND_MESSAGE
            window.CreateHandle(parameters);
            window.Hotkey = delegate(int id) { OnPressed(); };
        }

        public event Action Pressed;

        /// <summary>注册（或替换）快捷键；返回是否成功，失败时 error 说明原因（多半是被占用）。</summary>
        public bool Register(HotkeySpec spec, out string error)
        {
            error = null;
            Unregister();
            if (spec == null) return true;
            try
            {
                if (!Native.RegisterHotKey(window.Handle, HotkeyId, HotkeyText.Modifiers(spec), (uint)spec.Vk))
                {
                    int code = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    error = code == 1409
                        ? "快捷键已被其他程序占用，请换一个组合。"
                        : "注册快捷键失败（Win32 错误码 " + code + "）。";
                    if (log != null) log.Write(error);
                    return false;
                }
                registered = true;
                if (log != null) log.Write("已注册切换快捷键：" + HotkeyText.Format(spec));
                return true;
            }
            catch (Exception ex)
            {
                error = "注册快捷键异常：" + ex.Message;
                if (log != null) log.Write(error);
                return false;
            }
        }

        public void Unregister()
        {
            if (!registered) return;
            registered = false;
            try { Native.UnregisterHotKey(window.Handle, HotkeyId); }
            catch (Exception) { }
        }

        private void OnPressed()
        {
            Action handler = Pressed;
            if (handler != null) handler();
        }

        public void Dispose()
        {
            Unregister();
            if (window != null)
            {
                try { window.DestroyHandle(); }
                catch (Exception) { }
                window = null;
            }
        }
    }
}
