namespace PCL.Neo.Core.Diagnostics;

public enum CrashReason
{
    Unknown,
    OutOfMemory,
    IncompatibleJavaVersion,
    MissingModDependency,
    DuplicateMods,
    DriverOrGpuFailure
}

public sealed record CrashDiagnosis(
    CrashReason Reason,
    string Summary,
    string Advice);

/// <summary>
/// Minecraft 崩溃日志规则分析器。
/// 解析 latest.log 或 crash-reports 内容，自动识别典型错误并生成中文诊断指引。
/// </summary>
public static class CrashAnalyzer
{
    public static CrashDiagnosis Analyze(string logContent)
    {
        if (string.IsNullOrWhiteSpace(logContent))
            return new(CrashReason.Unknown, "未捕获到有效日志输出", "请检查游戏是否闪退或未正常启动。");

        if (logContent.Contains("java.lang.OutOfMemoryError", StringComparison.OrdinalIgnoreCase) ||
            logContent.Contains("There is insufficient memory for the Java Runtime Environment to continue", StringComparison.OrdinalIgnoreCase))
        {
            return new(
                CrashReason.OutOfMemory,
                "游戏运行内存耗尽 (OutOfMemoryError)",
                "请尝试在启动器设置中调大「最大内存限制」，若设备物理内存不足，请关闭其他高占用软件。");
        }

        if (logContent.Contains("has been compiled by a more recent version of the Java Runtime", StringComparison.OrdinalIgnoreCase) ||
            logContent.Contains("UnsupportedClassVersionError", StringComparison.OrdinalIgnoreCase))
        {
            return new(
                CrashReason.IncompatibleJavaVersion,
                "Java 运行时版本过低",
                "当前安装的 Mod 或游戏版本要求更高版本的 Java（例如 1.17+ 需 Java 17，1.20.5+ 需 Java 21），请切换所用的 Java 路径。");
        }

        if (logContent.Contains("ModResolutionException", StringComparison.OrdinalIgnoreCase) ||
            logContent.Contains("net.fabricmc.loader.impl.discovery.ModResolutionException", StringComparison.OrdinalIgnoreCase) ||
            logContent.Contains("requires", StringComparison.OrdinalIgnoreCase) && logContent.Contains("which is missing", StringComparison.OrdinalIgnoreCase))
        {
            return new(
                CrashReason.MissingModDependency,
                "Mod 缺失必要前置依赖",
                "Fabric/Forge 加载器报告缺少前置模组，请仔细阅读日志中的 requires 提示并补齐相应依赖。");
        }

        if (logContent.Contains("DuplicateModsFoundException", StringComparison.OrdinalIgnoreCase) ||
            logContent.Contains("Found duplicate default mod", StringComparison.OrdinalIgnoreCase))
        {
            return new(
                CrashReason.DuplicateMods,
                "存在重复的 Mod 文件",
                "mods 文件夹中存在相同模组的多个不同版本，请排查并删除多余版本。");
        }

        if (logContent.Contains("GLFW error 65542", StringComparison.OrdinalIgnoreCase) ||
            logContent.Contains("WGL: The driver does not appear to support OpenGL", StringComparison.OrdinalIgnoreCase))
        {
            return new(
                CrashReason.DriverOrGpuFailure,
                "显卡驱动缺少 OpenGL 支持",
                "Minecraft 无法初始化图形窗口，请更新显卡驱动，或安装包含 Mesa OpenGL 兼容层的组件。");
        }

        return new(
            CrashReason.Unknown,
            "未知错误或非典型崩溃",
            "建议查看完整日志中的 FATAL 或 Exception 堆栈跟踪以获取线索。");
    }
}
