using System.Text;
using PCL.Neo.Core.Minecraft.Manifest;

namespace PCL.Neo.Core.Minecraft;

/// <summary>
/// 把清单里的参数解析成真正的命令行。
/// <para>
/// 三条容易搞错的语义，全部按原版启动器实现：
/// </para>
/// <list type="number">
/// <item>带 rules 的参数只在规则命中时加入；<c>value</c> 为数组时整组共享同一套规则。</item>
/// <item>占位符替换后<b>结果为空串的参数要整条丢弃</b>（例如离线模式的
/// <c>${auth_access_token}</c>），否则会留下一个空参数把后续位置参数顶偏。</item>
/// <item>1.12 及更早只有 <c>minecraftArguments</c> 字符串，按空白切分后同样要展开占位符。</item>
/// </list>
/// </summary>
public static class LaunchArguments
{
    /// <summary>递归展开参数列表（含分组），跳过被规则排除的项。</summary>
    public static IReadOnlyList<string> Resolve(
        IReadOnlyList<Argument> arguments,
        RuntimeValue runtime,
        IReadOnlyDictionary<string, string> variables,
        bool strictPlaceholders = true)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(variables);

        var result = new List<string>(arguments.Count);
        foreach (var argument in arguments)
        {
            if (argument.Rules is not null && !RuleEvaluator.IsAllowed(argument.Rules, runtime))
                continue;

            if (argument.Group is not null)
            {
                // 整组共享外层规则，内层再各自判定
                result.AddRange(Resolve(argument.Group, runtime, variables, strictPlaceholders));
                continue;
            }

            if (argument.Text is null) continue;

            var expanded = Expand(argument.Text, variables, strictPlaceholders);
            if (expanded.Length > 0) result.Add(expanded);
        }

        return result;
    }

    /// <summary>展开旧版 <c>minecraftArguments</c>。</summary>
    public static IReadOnlyList<string> ResolveLegacy(
        string? minecraftArguments,
        IReadOnlyDictionary<string, string> variables,
        bool strictPlaceholders = true)
    {
        if (string.IsNullOrWhiteSpace(minecraftArguments)) return [];

        var result = new List<string>();
        foreach (var token in minecraftArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var expanded = Expand(token, variables, strictPlaceholders);
            if (expanded.Length > 0) result.Add(expanded);
        }

        return result;
    }

    /// <summary>
    /// 替换 <c>${name}</c>。未定义的占位符：严格模式抛异常（清单与启动器版本不匹配时
    /// 静默留原样会让游戏以诡异方式崩溃），宽松模式保留字面量。
    /// </summary>
    public static string Expand(string text, IReadOnlyDictionary<string, string> variables, bool strict = true)
    {
        if (!text.Contains("${", StringComparison.Ordinal)) return text;

        var sb = new StringBuilder(text.Length + 16);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '$' && i + 1 < text.Length && text[i + 1] == '{')
            {
                var end = text.IndexOf('}', i + 2);
                if (end > 0)
                {
                    var name = text[(i + 2)..end];
                    if (variables.TryGetValue(name, out var value))
                    {
                        sb.Append(value);
                        i = end;
                        continue;
                    }

                    if (strict)
                        throw new KeyNotFoundException($"清单引用了未知的启动参数占位符：${{{name}}}");
                }
            }

            sb.Append(text[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// 组装 JVM 参数：清单给的 jvm 参数里 <c>${classpath}</c> 是平台分隔的 classpath，
    /// 这里按当前平台用 <see cref="Path.PathSeparator"/> 连接。
    /// </summary>
    public static IReadOnlyList<string> BuildJvmArguments(
        ArgumentsSpec? spec,
        RuntimeValue runtime,
        IReadOnlyDictionary<string, string> variables,
        IReadOnlyList<string> classpath,
        bool strictPlaceholders = true)
    {
        var merged = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["classpath"] = string.Join(Path.PathSeparator, classpath)
        };

        foreach (var (key, value) in variables) merged[key] = value;

        // 现代清单自带 ${classpath}；旧清单没有 jvm 参数，需要回落到经典写法
        if (spec is null || spec.Jvm.Count == 0)
            return ["-Djava.class.path=" + merged["classpath"]];

        return Resolve(spec.Jvm, runtime, merged, strictPlaceholders);
    }
}
