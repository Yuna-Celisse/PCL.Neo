using System.Globalization;
using System.Text.RegularExpressions;

namespace PCL.Neo.Core.Minecraft.Java;

/// <summary>
/// 解析后的 Java 版本。
/// <para>
/// 注意 Java 9 之前的命名规则：<c>1.8.0_381</c> 的主版本是 <b>8</b> 而不是 1。
/// Minecraft 的版本要求（1.17+ 需 Java 16/17，1.20.5+ 需 Java 21 等）全部以主版本判断，
/// 因此这个归一化必须正确。
/// </para>
/// </summary>
/// <param name="Major">主版本，例如 Java 8 记为 8。</param>
/// <param name="Minor">次版本。</param>
/// <param name="Patch">更新号（Java 8 的 <c>_381</c> 归到这里）。</param>
/// <param name="Build">构建号。优先取版本串中 <c>+</c> 后的数字，其次取
/// <c>(build 17.0.9+9)</c> / <c>(build 1.8.0_381-b09)</c> 里的 <c>+N</c> 或 <c>-bN</c>；都没有则为 0。</param>
/// <param name="Raw">厂商给出的原始版本串，用于日志与 UI 展示。</param>
public sealed record JavaVersion(int Major, int Minor, int Patch, int Build, string Raw)
{
    /// <summary>主版本是否不低于要求。Minecraft 的版本要求只看主版本。</summary>
    public bool MeetsMinimum(int minimumMajor) => Major >= minimumMajor;

    public override string ToString() => Raw;
}

/// <summary>
/// 从 <c>java -version</c> 输出中提取版本号。各发行版（Temurin / Zulu / Corretto /
/// GraalVM / Semeru-J9 / Oracle / 发行版打包）首行措辞不同，但都遵循
/// <c>xxx version "N.N.N..."</c> 的带引号格式，因此只依赖引号内的内容。
/// </summary>
public static partial class JavaVersionParser
{
    [GeneratedRegex(@"version\s+""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex QuotedVersionRegex();

    [GeneratedRegex(@"^(\d+)(?:\.(\d+))?(?:\.(\d+))?")]
    private static partial Regex NumericPrefixRegex();

    [GeneratedRegex(@"\+(\d+)")]
    private static partial Regex BuildRegex();

    [GeneratedRegex(@"\(\s*build\s+([^)]+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex BuildParenthesisRegex();

    [GeneratedRegex(@"-b(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex LegacyBuildRegex();

    /// <summary>解析失败返回 null（发现阶段可能遇到非 Java 可执行文件，不应抛异常）。</summary>
    public static JavaVersion? TryParse(string? javaVersionOutput)
    {
        if (string.IsNullOrWhiteSpace(javaVersionOutput)) return null;

        var match = QuotedVersionRegex().Match(javaVersionOutput);
        if (!match.Success) return null;

        var raw = match.Groups[1].Value.Trim();
        var numeric = NumericPrefixRegex().Match(raw);
        if (!numeric.Success) return null;

        int Major, Minor, Patch;

        var first = int.Parse(numeric.Groups[1].Value, CultureInfo.InvariantCulture);
        var second = numeric.Groups[2].Success ? int.Parse(numeric.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        var third = numeric.Groups[3].Success ? ParseLeadingInt(numeric.Groups[3].Value) : 0;

        if (first == 1 && second is >= 1 and <= 9)
        {
            // 传统命名：1.8.0_381 → 主版本 8，更新号取 "_381"
            Major = second;
            Minor = third;
            var underscore = raw.IndexOf('_');
            Patch = underscore >= 0 ? ParseLeadingInt(raw[(underscore + 1)..]) : 0;
        }
        else
        {
            // 现代命名：17.0.9+9 → 17.0.9
            Major = first;
            Minor = second;
            Patch = third;
        }

        var buildNumber = ExtractBuild(raw, javaVersionOutput);

        return new JavaVersion(Major, Minor, Patch, buildNumber, raw);
    }

    public static JavaVersion Parse(string? javaVersionOutput)
        => TryParse(javaVersionOutput)
           ?? throw new FormatException($"无法从输出中解析 Java 版本：{javaVersionOutput}");

    /// <summary>
    /// 厂商的 build 号大多不在引号版本串里，而在第二行的 <c>(build ...)</c> 中，
    /// 因此取不到时回退到那里找。仅用于诊断展示，版本判定一律只看主版本。
    /// </summary>
    private static int ExtractBuild(string rawToken, string output)
    {
        var direct = BuildRegex().Match(rawToken);
        if (direct.Success) return ToInt(direct.Groups[1].Value);

        var paren = BuildParenthesisRegex().Match(output);
        if (!paren.Success) return 0;

        var inner = paren.Groups[1].Value; // Groups[0] 是整个匹配，捕获组从 1 开始
        var plus = BuildRegex().Match(inner);
        if (plus.Success) return ToInt(plus.Groups[1].Value);

        var legacy = LegacyBuildRegex().Match(inner);
        return legacy.Success ? ToInt(legacy.Groups[1].Value) : 0;

        static int ToInt(string text) => int.Parse(text, CultureInfo.InvariantCulture);
    }

    private static int ParseLeadingInt(string text)
    {
        var span = text.AsSpan();
        var end = 0;
        while (end < span.Length && char.IsAsciiDigit(span[end])) end++;
        return end == 0 ? 0 : int.Parse(span[..end], CultureInfo.InvariantCulture);
    }
}
