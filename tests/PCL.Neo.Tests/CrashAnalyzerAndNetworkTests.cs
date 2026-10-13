using System.Net;
using System.Net.Sockets;
using System.Text;
using PCL.Neo.Core.Diagnostics;
using PCL.Neo.Core.IO.Downloads;
using PCL.Neo.Core.Network;

namespace PCL.Neo.Tests;

public class CrashAnalyzerTests
{
    [Fact]
    public void Diagnoses_OutOfMemory()
    {
        var log = "Something happened\njava.lang.OutOfMemoryError: Java heap space\nProcess crashed";
        var diag = CrashAnalyzer.Analyze(log);

        Assert.Equal(CrashReason.OutOfMemory, diag.Reason);
        Assert.Contains("内存", diag.Summary);
    }

    [Fact]
    public void Diagnoses_IncompatibleJavaVersion()
    {
        var log = "Exception in thread \"main\" java.lang.UnsupportedClassVersionError: MyMod has been compiled by a more recent version of the Java Runtime (class file version 65.0)";
        var diag = CrashAnalyzer.Analyze(log);

        Assert.Equal(CrashReason.IncompatibleJavaVersion, diag.Reason);
        Assert.Contains("Java", diag.Summary);
    }

    [Fact]
    public void Diagnoses_MissingModDependency()
    {
        var log = "net.fabricmc.loader.impl.discovery.ModResolutionException: Mod 'example' requires 'fabric-api' which is missing!";
        var diag = CrashAnalyzer.Analyze(log);

        Assert.Equal(CrashReason.MissingModDependency, diag.Reason);
        Assert.Contains("前置", diag.Summary);
    }

    [Fact]
    public void Diagnoses_OpenGL_Error()
    {
        var log = "GLFW error 65542: WGL: The driver does not appear to support OpenGL";
        var diag = CrashAnalyzer.Analyze(log);

        Assert.Equal(CrashReason.DriverOrGpuFailure, diag.Reason);
        Assert.Contains("OpenGL", diag.Summary);
    }
}

public class MirrorSourcesTests
{
    [Fact]
    public void Bmclapi_Rewrites_Mojang_Hosts()
    {
        var rules = MirrorSources.CreateBmclapiRules();
        var originLib = "https://libraries.minecraft.net/com/google/gson.jar";

        var rewritten = rules.Select(r => r.Apply(originLib)).FirstOrDefault(r => r is not null);

        Assert.NotNull(rewritten);
        Assert.Equal("https://bmclapi2.bangbang93.com/libraries/com/google/gson.jar", rewritten);
    }

    [Fact]
    public void Mcbbs_Rewrites_Assets_Host()
    {
        var rules = MirrorSources.CreateMcbbsRules();
        var originAsset = "https://resources.download.minecraft.net/03/03abc.png";

        var rewritten = rules.Select(r => r.Apply(originAsset)).FirstOrDefault(r => r is not null);

        Assert.NotNull(rewritten);
        Assert.Equal("https://download.mcbbs.net/assets/03/03abc.png", rewritten);
    }
}
