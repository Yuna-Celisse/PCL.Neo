using System.Reflection;
using PCL.Neo.Core.Platform;

namespace PCL.Neo.Tests;

/// <summary>
/// 架构守卫。PCL-CE 的教训是“核心库”里混进了 31% 的 WPF 代码，
/// 导致所谓跨平台核心实际上仍然锁死在 Windows。这里用测试把边界钉住。
/// </summary>
public class ArchitectureTests
{
    [Fact]
    public void Core_Does_Not_Reference_Avalonia_Or_Any_Ui_Framework()
    {
        var referenced = typeof(PlatformInfo).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToList();

        var offenders = referenced
            .Where(n => n.StartsWith("Avalonia", StringComparison.OrdinalIgnoreCase)
                        || n.StartsWith("PresentationCore", StringComparison.OrdinalIgnoreCase)
                        || n.StartsWith("System.Windows", StringComparison.OrdinalIgnoreCase)
                        || n.StartsWith("WindowsBase", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(offenders);
    }
}
