using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DisplayDock
{
    /// <summary>
    /// 显示器输入口（VCP 0x60）的识别与命名。
    /// 名称来自 VESA MCCS 标准输入源代号；能力字符串解析复用 v2.1 已验证的实现。
    /// </summary>
    public static class PortCatalog
    {
        /// <summary>MCCS 标准输入源代号 → 中文/通用名称。</summary>
        public static string Label(uint value)
        {
            switch (value)
            {
                case 0x01: return "VGA（模拟1）";
                case 0x02: return "VGA（模拟2）";
                case 0x03: return "DVI1";
                case 0x04: return "DVI2";
                case 0x05: return "复合视频1";
                case 0x06: return "复合视频2";
                case 0x07: return "S-Video1";
                case 0x08: return "S-Video2";
                case 0x0F: return "DisplayPort1";
                case 0x10: return "DisplayPort2";
                case 0x11: return "HDMI1";
                case 0x12: return "HDMI2";
                case 0x13: return "HDMI3";
                case 0x14: return "HDMI4";
                case 0x1B: return "USB-C";
                case 0x1C: return "USB-C2";
                case 0x1D: return "USB-C3";
                default: return "输入0x" + value.ToString("X2");
            }
        }

        /// <summary>读不到能力字符串时的兜底端口表（常见接口，仅供用户手动挑选）。</summary>
        public static List<PortInfo> DefaultPorts()
        {
            var values = new uint[] { 0x0F, 0x10, 0x11, 0x12, 0x13, 0x1B, 0x01, 0x03 };
            var list = new List<PortInfo>();
            foreach (uint value in values)
            {
                var port = new PortInfo();
                port.Value = value;
                port.Name = Label(value);
                list.Add(port);
            }
            return list;
        }

        /// <summary>
        /// 从 MCCS 能力字符串里解析 vcp(60( ... )) 的取值列表（显示器真实支持的输入口）。
        /// 解析不到时返回空列表，由调用方决定是否用兜底表。
        /// </summary>
        public static List<PortInfo> ParseInputs(string caps)
        {
            var result = new List<PortInfo>();
            if (String.IsNullOrEmpty(caps)) return result;
            Match vcp = Regex.Match(caps, @"\bvcp\s*\(", RegexOptions.IgnoreCase);
            if (!vcp.Success) return result;
            int start = vcp.Index + vcp.Length;
            int depth = 1, end = start;
            for (; end < caps.Length && depth > 0; end++)
            {
                if (caps[end] == '(') depth++;
                else if (caps[end] == ')') depth--;
            }
            if (depth != 0) return result;
            string section = caps.Substring(start, end - start - 1);
            // 只读 vcp(...) 顶层的能力项，避免把 60(...) 里嵌套的其它内容当端口。
            for (int i = 0; i < section.Length; )
            {
                while (i < section.Length && Char.IsWhiteSpace(section[i])) i++;
                int tokenStart = i;
                while (i < section.Length && Uri.IsHexDigit(section[i])) i++;
                string token = section.Substring(tokenStart, i - tokenStart);
                while (i < section.Length && Char.IsWhiteSpace(section[i])) i++;
                if (i < section.Length && section[i] == '(')
                {
                    int valuesStart = ++i, nesting = 1;
                    while (i < section.Length && nesting > 0)
                    {
                        if (section[i] == '(') nesting++;
                        else if (section[i] == ')') nesting--;
                        i++;
                    }
                    if (token.Equals("60", StringComparison.OrdinalIgnoreCase) && nesting == 0)
                    {
                        string values = section.Substring(valuesStart, i - valuesStart - 1);
                        foreach (string item in Regex.Split(values.Trim(), @"\s+"))
                        {
                            uint parsed;
                            if (Regex.IsMatch(item, @"\A[0-9a-fA-F]{1,2}\z") &&
                                UInt32.TryParse(item, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed) &&
                                !Contains(result, parsed))
                            {
                                var port = new PortInfo();
                                port.Value = parsed;
                                port.Name = Label(parsed);
                                result.Add(port);
                            }
                        }
                        return result;
                    }
                }
                else if (i == tokenStart) i++;
            }
            return result;
        }

        private static bool Contains(List<PortInfo> list, uint value)
        {
            foreach (PortInfo port in list) if (port.Value == value) return true;
            return false;
        }

        /// <summary>从能力字符串里提取显示器型号名（prot/type 之外的 model(...) 字段）。</summary>
        public static string ParseModel(string caps)
        {
            if (String.IsNullOrEmpty(caps)) return null;
            Match match = Regex.Match(caps, @"\bmodel\s*\(\s*([^)]+?)\s*\)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
