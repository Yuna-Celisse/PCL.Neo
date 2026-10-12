using System.Text;

namespace PCL.Neo.Core.Configuration;

/// <summary>
/// 行式 INI 文档模型。
/// <para>
/// 设计取向：<b>无损往返</b>优先于“漂亮”。解析时把每一行原样保留（注释、空行、
/// 未知键、键的先后顺序），写回时按原顺序输出，只替换被改动的那一行。
/// 这样用户手工编辑过的配置文件不会被启动器悄悄重排。
/// </para>
/// <para>
/// 键名比较为 <b>ordinal 大小写敏感</b>：<c>Foo</c> 与 <c>FOO</c> 是两个不同的键。
/// 这是刻意的——Linux 文件系统大小写敏感，若采用 Windows 式大小写不敏感比较，
/// 同一份配置在两个平台上的解析结果会不一致。
/// </para>
/// </summary>
public sealed class IniDocument
{
    private enum LineKind { Comment, Blank, Section, Pair }

    /// <param name="Text">原始整行文本，用于把没被改过的行逐字节写回。</param>
    /// <param name="Dirty">值被改动过，必须重新渲染而不是回写原文。</param>
    private sealed record Line(LineKind Kind, string Text, string Section, string Key, string Value, bool Dirty = false);

    private readonly List<Line> _lines = [];

    /// <summary>未显式指定节时使用的节名。</summary>
    public const string GlobalSection = "";

    public static IniDocument Parse(string text)
    {
        var doc = new IniDocument();
        var section = GlobalSection;

        if (text.Length == 0) return doc;

        // 以换行结尾的文本，Split 会多出一个空尾元素——它不是真实存在的一行，
        // 若不剔除，每次读写往返都会凭空多一个空行。
        var physical = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var count = physical.Length;
        if (count > 1 && physical[^1].Length == 0) count--;

        for (var index = 0; index < count; index++)
        {
            var raw = physical[index];
            var trimmed = raw.Trim();

            if (trimmed.Length == 0)
            {
                doc._lines.Add(new Line(LineKind.Blank, raw, section, "", ""));
                continue;
            }

            if (trimmed is [',', ..] or [';', ..])
            {
                doc._lines.Add(new Line(LineKind.Comment, raw, section, "", ""));
                continue;
            }

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']') && trimmed.Length > 2)
            {
                section = trimmed[1..^1].Trim();
                doc._lines.Add(new Line(LineKind.Section, raw, section, "", ""));
                continue;
            }

            var eq = trimmed.IndexOf('=');
            if (eq > 0)
            {
                var key = trimmed[..eq].Trim();
                doc._lines.Add(new Line(LineKind.Pair, raw, section, key, DecodeValue(trimmed[(eq + 1)..])));
            }
            else
            {
                // 无法识别的行按注释对待，原样保留，绝不丢弃用户数据
                doc._lines.Add(new Line(LineKind.Comment, raw, section, "", ""));
            }
        }

        return doc;
    }

    public bool TryGetValue(string section, string key, out string value)
    {
        for (var i = _lines.Count - 1; i >= 0; i--)
        {
            var line = _lines[i];
            if (line.Kind == LineKind.Pair
                && string.Equals(line.Section, section, StringComparison.Ordinal)
                && string.Equals(line.Key, key, StringComparison.Ordinal))
            {
                value = line.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    public void SetValue(string section, string key, string value)
    {
        for (var i = _lines.Count - 1; i >= 0; i--)
        {
            var line = _lines[i];
            if (line.Kind == LineKind.Pair
                && string.Equals(line.Section, section, StringComparison.Ordinal)
                && string.Equals(line.Key, key, StringComparison.Ordinal))
            {
                _lines[i] = line with { Value = value, Dirty = true };
                return;
            }
        }

        var newLine = new Line(LineKind.Pair, "", section, key, value, Dirty: true);

        // 追加到该节末尾；节不存在则连同节头一起新建在文档末尾
        var lastOfSection = -1;
        var hasSection = false;
        for (var i = 0; i < _lines.Count; i++)
        {
            if (_lines[i].Kind == LineKind.Section && _lines[i].Section == section) hasSection = true;
            if (_lines[i].Section == section && _lines[i].Kind is LineKind.Pair or LineKind.Comment or LineKind.Blank)
                lastOfSection = i;
        }

        if (!hasSection && section != GlobalSection)
        {
            _lines.Add(new Line(LineKind.Section, $"[{section}]", section, "", ""));
            _lines.Add(newLine);
        }
        else if (lastOfSection >= 0)
        {
            _lines.Insert(lastOfSection + 1, newLine);
        }
        else
        {
            _lines.Add(newLine);
        }
    }

    public bool Remove(string section, string key)
    {
        for (var i = _lines.Count - 1; i >= 0; i--)
        {
            var line = _lines[i];
            if (line.Kind == LineKind.Pair
                && string.Equals(line.Section, section, StringComparison.Ordinal)
                && string.Equals(line.Key, key, StringComparison.Ordinal))
            {
                _lines.RemoveAt(i);
                return true;
            }
        }

        return false;
    }

    public IEnumerable<string> Keys(string section)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in _lines)
        {
            if (line.Kind == LineKind.Pair
                && string.Equals(line.Section, section, StringComparison.Ordinal)
                && seen.Add(line.Key))
            {
                yield return line.Key;
            }
        }
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        foreach (var line in _lines)
        {
            sb.Append(line.Kind switch
            {
                // 没被改过的行逐字节回写：保留用户手写的空格、注释与顺序，
                // 也避免启动器在每次保存时把整个文件重排一遍。
                LineKind.Pair when line.Dirty => $"{line.Key}={EncodeValue(line.Value)}",
                _ => line.Text
            });
            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// 值编码：转义反斜杠与换行；若首尾含空白，则整体加引号——
    /// 因为解析时会裁掉行尾空白，不加引号就会静默丢失用户写的首尾空格。
    /// </summary>
    private static string EncodeValue(string value)
    {
        var encoded = Escape(value);
        if (encoded.Length > 0 && encoded != encoded.Trim())
            return "\"" + encoded + "\"";
        return encoded;
    }

    /// <summary>值解码：先剥引号，再还原转义。</summary>
    private static string DecodeValue(string rawValue)
    {
        var value = rawValue.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1];
        return Unescape(value);
    }

    private static string Escape(string value) =>
        value.Contains('\\', StringComparison.Ordinal) || value.Contains('\n', StringComparison.Ordinal) || value.Contains('\r', StringComparison.Ordinal)
            ? value.Replace("\\", "\\\\", StringComparison.Ordinal)
                   .Replace("\r", "\\r", StringComparison.Ordinal)
                   .Replace("\n", "\\n", StringComparison.Ordinal)
            : value;

    /// <summary>未知转义序列原样保留，避免把老文件里的字面反斜杠吃掉。</summary>
    private static string Unescape(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal)) return value;

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i == value.Length - 1)
            {
                sb.Append(value[i]);
                continue;
            }

            var next = value[i + 1];
            switch (next)
            {
                case '\\': sb.Append('\\'); i++; break;
                case 'n': sb.Append('\n'); i++; break;
                case 'r': sb.Append('\r'); i++; break;
                default: sb.Append(value[i]); break; // 保持字面量
            }
        }

        return sb.ToString();
    }
}
