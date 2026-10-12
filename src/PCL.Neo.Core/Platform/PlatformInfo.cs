using System.Runtime.InteropServices;

namespace PCL.Neo.Core.Platform;

/// <summary>启动器运行于其上的操作系统类别。</summary>
public enum PlatformKind
{
    Unknown = 0,
    Windows,
    MacOS,
    Linux
}

/// <summary>
/// CPU 架构类别。PCL.Neo 需要覆盖 x64 / x86 / ARM64 / LoongArch64 / RISC-V64，
/// 其中 RISC-V 尚未出现在 <see cref="RuntimeInformation"/> 的 <c>Architecture</c> 枚举中，
/// 因此这里用字符串探测兜底。
/// </summary>
public enum ArchitectureKind
{
    Unknown = 0,
    X86,
    X64,
    Arm64,
    LoongArch64,
    RiscV64,
    Wasm
}

/// <summary>
/// 当前运行环境的平台信息。全部为一次性求值的只读属性，可安全在任意线程访问。
/// </summary>
public static class PlatformInfo
{
    public static PlatformKind Platform { get; } = DetectPlatform();

    public static ArchitectureKind Architecture { get; } = DetectArchitecture();

    public static bool IsWindows => Platform == PlatformKind.Windows;

    public static bool IsMacOS => Platform == PlatformKind.MacOS;

    public static bool IsLinux => Platform == PlatformKind.Linux;

    /// <summary>形如 <c>Linux 6.8.0 (ubuntu 24.04)</c> 的系统描述，用于日志与崩溃报告。</summary>
    public static string OsDescription => RuntimeInformation.OSDescription;

    /// <summary>形如 <c>Linux-x64</c> 的简短标识，用于更新通道与产物命名。</summary>
    public static string Describe() => $"{Platform}-{Architecture}";

    private static PlatformKind DetectPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return PlatformKind.Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return PlatformKind.MacOS;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return PlatformKind.Linux;
        return PlatformKind.Unknown;
    }

    private static ArchitectureKind DetectArchitecture()
    {
        // 用枚举的文本形式匹配，避免依赖具体运行时版本是否定义了 LoongArch64 等成员。
        return RuntimeInformation.ProcessArchitecture.ToString() switch
        {
            "X86" => ArchitectureKind.X86,
            "X64" => ArchitectureKind.X64,
            "Arm" or "Arm64" => ArchitectureKind.Arm64,
            "LoongArch64" => ArchitectureKind.LoongArch64,
            "Wasm" => ArchitectureKind.Wasm,
            _ => RuntimeInformation.OSDescription.Contains("riscv", StringComparison.OrdinalIgnoreCase)
                ? ArchitectureKind.RiscV64
                : ArchitectureKind.Unknown
        };
    }
}
