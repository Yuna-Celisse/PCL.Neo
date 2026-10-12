using PCL.Neo.Core.Minecraft.Manifest;

namespace PCL.Neo.Tests;

public class LibraryResolutionTests
{
    private static readonly RuntimeValue LinuxX64 = new("linux", "6.5.0", "x86");
    private static readonly RuntimeValue OsxArm64 = new("osx", "23.1.0", "arm-64");

    [Fact]
    public void Modern_Library_Resolves_Artifact_Url_And_Hash()
    {
        var library = new LibraryInfo
        {
            Name = "com.google.code.gson:gson:2.10.1",
            Downloads = new LibraryDownloads
            {
                Artifact = new DownloadSpec("https://repo/gson-2.10.1.jar", "abc", 280991)
            }
        };

        var artifact = Assert.Single(library.ResolveArtifacts(LinuxX64));
        Assert.Equal("abc", artifact.Sha1);
        Assert.Equal(280991, artifact.Size);
        Assert.Equal("https://repo/gson-2.10.1.jar", artifact.AbsoluteUrl);
        Assert.Null(artifact.Classifier);
    }

    [Fact]
    public void Rules_Filter_Out_Wrong_Platform_Libraries()
    {
        var library = new LibraryInfo
        {
            Name = "org.lwjgl:lwjgl:3.3.1:natives-macos-arm64",
            Rules = [new Rule("allow", new OsRule(Name: "osx", Arch: "arm-64"))]
        };

        Assert.True(library.IsAllowed(OsxArm64));
        Assert.False(library.IsAllowed(LinuxX64));
    }

    [Fact]
    public void Legacy_Natives_Map_Selects_Classifier_With_Bits()
    {
        var library = new LibraryInfo
        {
            Name = "org.lwjgl:lwjgl-platform:2.9.1",
            Natives = new Dictionary<string, string>
            {
                ["linux"] = "natives-linux",
                ["windows"] = "natives-windows-${arch}",
                ["osx"] = "natives-macos"
            }
        };

        Assert.Equal("natives-linux", library.ResolveClassifier(LinuxX64));
        Assert.Equal("natives-macos", library.ResolveClassifier(OsxArm64));
        Assert.Equal("natives-windows-64",
            library.ResolveClassifier(new RuntimeValue("windows", "10.0", "x86", ArchBits: "64")));
        Assert.Equal("natives-windows-32",
            library.ResolveClassifier(new RuntimeValue("windows", "6.1", "x86", ArchBits: "32")));
    }

    [Fact]
    public void Legacy_Classifier_Path_Is_Derived_From_Coordinate()
    {
        var library = new LibraryInfo
        {
            Name = "org.lwjgl:lwjgl-platform:2.9.1",
            Natives = new Dictionary<string, string> { ["linux"] = "natives-linux" },
            Url = "https://legacy.repo/"
        };

        // 原生库没有 artifact，只应有 classifier 一项
        var artifact = Assert.Single(library.ResolveArtifacts(LinuxX64));
        Assert.Equal("natives-linux", artifact.Classifier);
        Assert.Equal("org/lwjgl/lwjgl-platform/2.9.1/lwjgl-platform-2.9.1-natives-linux.jar", artifact.RelativePath);
        Assert.Equal("https://legacy.repo/org/lwjgl/lwjgl-platform/2.9.1/lwjgl-platform-2.9.1-natives-linux.jar",
            artifact.AbsoluteUrl);
        Assert.Null(artifact.Sha1); // 旧清单没有哈希，调用方必须知道这一点
    }

    [Fact]
    public void Non_Native_Library_Without_Downloads_Uses_Coordinate_Path()
    {
        var library = new LibraryInfo { Name = "io.netty:netty-all:4.0.23.Final" };

        var artifact = Assert.Single(library.ResolveArtifacts(LinuxX64));
        Assert.Equal("io/netty/netty-all/4.0.23.Final/netty-all-4.0.23.Final.jar", artifact.RelativePath);
    }

    [Fact]
    public void Classifier_From_Modern_Downloads_Wins_Over_Derivation()
    {
        var library = new LibraryInfo
        {
            Name = "org.lwjgl:lwjgl:3.3.1",
            Natives = new Dictionary<string, string> { ["osx"] = "natives-macos-arm64" },
            Downloads = new LibraryDownloads
            {
                Artifact = new DownloadSpec("https://repo/lwjgl-3.3.1.jar", "a", 1),
                Classifiers = new Dictionary<string, DownloadSpec>
                {
                    ["natives-macos-arm64"] = new DownloadSpec("https://repo/lwjgl-3.3.1-natives-macos-arm64.jar", "b", 2)
                }
            }
        };

        var artifacts = library.ResolveArtifacts(OsxArm64).ToList();

        Assert.Equal(2, artifacts.Count);
        Assert.Equal("a", artifacts[0].Sha1);
        Assert.Equal("natives-macos-arm64", artifacts[1].Classifier);
        Assert.Equal("b", artifacts[1].Sha1);
    }
}
