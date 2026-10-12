using PCL.Neo.Core.Minecraft.Java;
using PCL.Neo.Core.Platform;

namespace PCL.Neo.Tests;

/// <summary>
/// 与 CI 上是否真的装了 Java 无关——这些用例只校验发现逻辑的结构不变量，
/// 因此不会因 runner 环境差异而变红。
/// </summary>
[Collection("Environment")]
public class JavaLocatorTests
{
    [Fact]
    public void ExecutableName_Matches_Platform()
    {
        Assert.Equal(PlatformInfo.IsWindows ? "java.exe" : "java", JavaLocator.ExecutableName);
    }

    [Fact]
    public void Candidates_Are_Sorted_Unique_And_Well_Formed()
    {
        var candidates = JavaLocator.EnumerateCandidates();

        Assert.Distinct(candidates.Select(c => c.Executable));
        Assert.Equal(candidates.OrderBy(c => c.Executable, StringComparer.Ordinal), candidates);

        foreach (var candidate in candidates)
        {
            Assert.True(File.Exists(candidate.Executable), $"候选指向不存在的文件：{candidate.Executable}");
            Assert.Equal(
                Path.Combine("bin", JavaLocator.ExecutableName),
                Path.GetRelativePath(candidate.Home, candidate.Executable));
            Assert.Null(candidate.Version); // 未经探测前不得带上版本，防止“自动信任扫描到的可执行文件”
        }
    }

    [Fact]
    public void Java_Home_Is_Discovered()
    {
        using var temp = TempDirectory.Create();
        var home = temp.PathOf("jdk-17");
        Directory.CreateDirectory(Path.Combine(home, "bin"));
        File.WriteAllText(Path.Combine(home, "bin", JavaLocator.ExecutableName), "#!/bin/sh\n");

        var previous = Environment.GetEnvironmentVariable("JAVA_HOME");
        try
        {
            Environment.SetEnvironmentVariable("JAVA_HOME", home);
            var found = JavaLocator.EnumerateCandidates()
                .Where(c => string.Equals(c.Home, Path.GetFullPath(home), StringComparison.Ordinal))
                .ToList();

            var candidate = Assert.Single(found);
            Assert.Equal(JavaDiscoverySource.JavaHome, candidate.Source);
        }
        finally
        {
            Environment.SetEnvironmentVariable("JAVA_HOME", previous);
        }
    }

    [Fact]
    public void Missing_Standard_Roots_Are_Skipped_Silently()
    {
        // 不抛异常即可：扫描阶段遇到无权限或不存在的目录是常态
        var _ = JavaLocator.EnumerateCandidates();
    }
}
