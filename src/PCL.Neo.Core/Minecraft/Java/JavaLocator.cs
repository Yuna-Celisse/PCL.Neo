using PCL.Neo.Core.Platform;
using PCL.Neo.Core.Processes;

namespace PCL.Neo.Core.Minecraft.Java;

public enum JavaDiscoverySource
{
    /// <summary>JAVA_HOME 环境变量。</summary>
    JavaHome,
    /// <summary>PATH 中可找到的 java。</summary>
    Path,
    /// <summary>系统标准安装目录扫描。</summary>
    StandardLocation,
    /// <summary>用户在设置里手动添加。</summary>
    Manual
}

/// <summary>一处已发现的 Java。</summary>
/// <param name="Executable">java 可执行文件绝对路径。</param>
/// <param name="Home">JAVA_HOME（可执行文件所在目录的上一级）。</param>
/// <param name="Source">发现途径。</param>
/// <param name="Version">探测结果；未探测时为 null。</param>
public sealed record JavaInstallation(
    string Executable,
    string Home,
    JavaDiscoverySource Source,
    JavaVersion? Version = null)
{
    public JavaInstallation WithVersion(JavaVersion? version) => this with { Version = version };
}

/// <summary>
/// Java 安装发现。
/// <para>
/// 与 PCL-CE 的关键差异：CE 依赖注册表与 <c>System.Management</c>（WMI），在
/// Linux/macOS 上完全不可用。这里只做“环境变量 + 各平台标准目录”扫描，
/// 不执行任何来自被扫描目录的内容——自动信任扫描到的可执行文件本身就是 CE 正在修的一个漏洞。
/// 因此发现结果一律标记为未验证，必须由调用方显式探测后才可用于启动。
/// </para>
/// </summary>
public static class JavaLocator
{
    /// <summary>各平台 java 可执行文件名。</summary>
    public static string ExecutableName => PlatformInfo.IsWindows ? "java.exe" : "java";

    /// <summary>
    /// 枚举候选 java 可执行文件。纯查询，不启动进程；目录不存在时静默跳过。
    /// 结果按路径去重并排序，保证跨平台行为一致。
    /// </summary>
    public static IReadOnlyList<JavaInstallation> EnumerateCandidates()
    {
        var found = new Dictionary<string, JavaInstallation>(StringComparer.Ordinal);

        void Add(string executable, JavaDiscoverySource source)
        {
            if (string.IsNullOrWhiteSpace(executable)) return;
            string full;
            try { full = Path.GetFullPath(executable); }
            catch { return; }

            if (!File.Exists(full)) return;
            if (found.ContainsKey(full)) return;

            var home = DeriveHome(full);
            if (home is null) return;
            found.Add(full, new JavaInstallation(full, home, Source: source));
        }

        // 1) JAVA_HOME
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
            Add(Path.Combine(javaHome!, "bin", ExecutableName), JavaDiscoverySource.JavaHome);

        // 2) PATH
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(pathVar))
        {
            foreach (var dir in pathVar!.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try { Add(Path.Combine(dir, ExecutableName), JavaDiscoverySource.Path); }
                catch (ArgumentException) { /* PATH 里可能有非法片段 */ }
            }
        }

        // 3) 平台标准位置
        if (PlatformInfo.IsLinux)
        {
            foreach (var root in new[] { "/usr/lib/jvm", "/usr/java", "/opt/java", "/opt/jdk" })
                AddFromChildren(root, "bin", ExecutableName, JavaDiscoverySource.StandardLocation, Add);

            // 发行版通过 alternatives 管理的链接
            Add("/usr/bin/java", JavaDiscoverySource.StandardLocation);

            var sdkman = Path.Combine(AppPaths.UserProfile, ".sdkman", "candidates", "java");
            AddFromChildren(sdkman, "bin", ExecutableName, JavaDiscoverySource.StandardLocation, Add);
        }
        else if (PlatformInfo.IsMacOS)
        {
            foreach (var root in new[]
                     {
                         "/Library/Java/JavaVirtualMachines",
                         "/System/Library/Java/JavaVirtualMachines"
                     })
            {
                AddFromChildren(root, "Contents", "bin", ExecutableName, JavaDiscoverySource.StandardLocation, Add);
            }

            AddFromChildren(Path.Combine(AppPaths.UserProfile, "Library", "Java"),
                "bin", ExecutableName, JavaDiscoverySource.StandardLocation, Add);
        }
        else if (PlatformInfo.IsWindows)
        {
            foreach (var pf in new[]
                     {
                         Environment.GetEnvironmentVariable("ProgramFiles"),
                         Environment.GetEnvironmentVariable("ProgramFiles(x86)")
                     })
            {
                if (string.IsNullOrWhiteSpace(pf)) continue;
                // Temurin / Zulu / Corretto / Microsoft / BellSoft / 老式 Java 目录
                foreach (var vendor in new[]
                         {
                             "Eclipse Adoptium", "Eclipse Foundation", "Zulu", "Amazon Corretto",
                             "Microsoft", "BellSoft", "Java", "AdoptOpenJDK", "Semeru"
                         })
                {
                    AddFromChildren(Path.Combine(pf!, vendor), "bin", ExecutableName,
                        JavaDiscoverySource.StandardLocation, Add);
                }
            }
        }

        var list = found.Values.ToList();
        list.Sort(static (a, b) => string.CompareOrdinal(a.Executable, b.Executable));
        return list;
    }

    /// <summary>
    /// 运行 <c>java -version</c> 解析版本。调用方在把某个 Java 用于启动前必须先探测成功。
    /// </summary>
    public static async Task<JavaInstallation> ProbeAsync(
        JavaInstallation installation,
        IProcessRunner runner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runner);

        using var process = await runner.StartAsync(
            new ProcessLaunchRequest(installation.Executable, "-version"),
            cancellationToken).ConfigureAwait(false);

        // 超时保护：损坏的 JRE 可能挂住，扫描阶段不该被单个安装拖死
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.KillTree();
            return installation.WithVersion(null);
        }

        // java -version 历史上输出到 stderr，两个流都要看
        var version = JavaVersionParser.TryParse(process.StandardOutput)
                      ?? JavaVersionParser.TryParse(process.StandardError);

        return installation.WithVersion(version);
    }

    /// <summary>从可执行文件路径推导 JAVA_HOME；无法推导（层级不足）时返回 null。</summary>
    private static string? DeriveHome(string executable)
    {
        var bin = Path.GetDirectoryName(executable);
        var home = string.IsNullOrEmpty(bin) ? null : Path.GetDirectoryName(bin);
        return string.IsNullOrEmpty(home) ? null : home;
    }

    private static void AddFromChildren(
        string root,
        string middle,
        string leaf,
        JavaDiscoverySource source,
        Action<string, JavaDiscoverySource> add)
    {
        if (!Directory.Exists(root)) return;

        string[] children;
        try { children = Directory.GetDirectories(root); }
        catch (UnauthorizedAccessException) { return; }
        catch (IOException) { return; }

        foreach (var child in children)
        {
            var path = middle.Length == 0
                ? Path.Combine(child, leaf)
                : Path.Combine(child, middle, leaf);
            add(path, source);
        }
    }

    // 便于 macOS 上多一层 Contents 的重载
    private static void AddFromChildren(
        string root,
        string first,
        string second,
        string leaf,
        JavaDiscoverySource source,
        Action<string, JavaDiscoverySource> add)
    {
        if (!Directory.Exists(root)) return;

        string[] children;
        try { children = Directory.GetDirectories(root); }
        catch (UnauthorizedAccessException) { return; }
        catch (IOException) { return; }

        foreach (var child in children)
            add(Path.Combine(child, first, second, leaf), source);
    }
}
