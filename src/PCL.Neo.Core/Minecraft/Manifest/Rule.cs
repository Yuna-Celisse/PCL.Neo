using System.Text.RegularExpressions;

namespace PCL.Neo.Core.Minecraft.Manifest;

/// <summary>规则判定所需的运行时事实。</summary>
/// <param name="OsName">windows / osx / linux（小写）。</param>
/// <param name="OsVersion">供 <c>os.version</c> 正则匹配的字符串。</param>
/// <param name="OsArch">
/// x86 / arm-64。注意 <b>64 位 x86 也要上报为 x86</b>：
/// 清单里 <c>{"os":{"arch":"x86","name":"osx"}}</c> 这类规则的作用是把 Apple Silicon
/// 排除在 x86 专用原生库之外，若把 x64 映射成别的值就会错误地加载 Intel 库。
/// </param>
/// <param name="Features">已启用的启动特性（如 is_demo_user）。</param>
public sealed record RuntimeValue(
    string OsName,
    string OsVersion,
    string OsArch,
    IReadOnlySet<string>? Features = null,
    /// <summary>指针位数（"32" / "64"），供旧清单里的 <c>${arch}</c> 占位使用。
    /// 不能从 OsArch 推导：x64 与 x86 在规则里都上报为 x86。</summary>
    string ArchBits = "64")
{

    /// <summary>
    /// 用当前真实运行环境构造。64 位 x86 上报为 x86 —— 见 OsArch 的说明。
    /// </summary>
    public static RuntimeValue ForCurrentPlatform(PCL.Neo.Core.Platform.PlatformKind platform,
                                                  PCL.Neo.Core.Platform.ArchitectureKind architecture,
                                                  string? osVersion = null)
        => new(
            OsName: platform switch
            {
                PCL.Neo.Core.Platform.PlatformKind.Windows => "windows",
                PCL.Neo.Core.Platform.PlatformKind.MacOS => "osx",
                _ => "linux"
            },
            OsVersion: osVersion ?? Environment.OSVersion.Version.ToString(),
            OsArch: architecture switch
            {
                PCL.Neo.Core.Platform.ArchitectureKind.Arm64 => "arm-64",
                PCL.Neo.Core.Platform.ArchitectureKind.X86 => "x86",
                PCL.Neo.Core.Platform.ArchitectureKind.X64 => "x86",
                _ => architecture.ToString().ToLowerInvariant()
            },
            ArchBits: architecture == PCL.Neo.Core.Platform.ArchitectureKind.X86 ? "32" : "64");
}

/// <summary>清单中的一条 allow / disallow 规则。</summary>
public sealed record Rule(string Action, OsRule? Os = null, IReadOnlyDictionary<string, bool>? Features = null)
{
    /// <summary>无任何条件时，规则无条件命中。</summary>
    public bool Matches(RuntimeValue runtime)
    {
        if (Os is null && Features is null) return true;

        if (Os is not null && !Os.Matches(runtime)) return false;

        if (Features is not null)
        {
            foreach (var (name, required) in Features)
            {
                var present = runtime.Features?.Contains(name) ?? false;
                if (required != present) return false;
            }
        }

        return true;
    }
}

/// <summary>规则里的 <c>os</c> 条件。</summary>
public sealed record OsRule(string? Name = null, string? Version = null, string? Arch = null)
{
    public bool Matches(RuntimeValue runtime)
    {
        if (Name is not null && !string.Equals(Name, runtime.OsName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (Arch is not null)
        {
            // 清单里出现过 "x86|arm-64" 这类或值
            var allowed = Arch.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!allowed.Any(a => string.Equals(a, runtime.OsArch, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        if (Version is not null)
        {
            if (!Regex.IsMatch(runtime.OsVersion, Version, RegexOptions.IgnoreCase | RegexOptions.NonBacktracking))
                return false;
        }

        return true;
    }
}

/// <summary>
/// 规则求值。语义（Mojang 启动器规范）：
/// <list type="bullet">
/// <item>没有 rules → 允许。</item>
/// <item>有 rules → 初始为<b>禁止</b>，按顺序遍历，每条命中的规则把状态改成它自己的 action；
/// 最终以最后一条命中的规则为准。</item>
/// </list>
/// 把“任意 allow 命中即通过”当成实现是常见错误，那样会让排在后面的 disallow 失效。
/// </summary>
public static class RuleEvaluator
{
    public static bool IsAllowed(IReadOnlyList<Rule>? rules, RuntimeValue runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (rules is null || rules.Count == 0) return true;

        var allowed = false;
        foreach (var rule in rules)
        {
            if (!rule.Matches(runtime)) continue;
            allowed = string.Equals(rule.Action, "allow", StringComparison.OrdinalIgnoreCase);
        }

        return allowed;
    }
}
