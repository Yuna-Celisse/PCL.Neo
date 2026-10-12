using PCL.Neo.Core.Minecraft;
using PCL.Neo.Core.Minecraft.Manifest;
using PCL.Neo.Core.Platform;

namespace PCL.Neo.Tests;

public class ManifestTests
{
    private const string ModernVersion = """
        {
          "id": "1.20.1",
          "type": "release",
          "mainClass": "net.minecraft.client.main.Main",
          "assets": "12",
          "javaVersion": { "majorVersion": 17 },
          "downloads": {
            "client": { "sha1": "aa", "size": 22, "url": "https://example.invalid/1.20.1-client.jar" },
            "assetIndex": { "id": "12", "sha1": "bb", "size": 33, "url": "https://example.invalid/12.json" }
          },
          "libraries": [
            { "name": "com.google.code.gson:gson:2.10.1",
              "downloads": { "artifact": { "path": "com/google/code/gson/gson/2.10.1/gson-2.10.1.jar",
                                          "sha1": "cc", "size": 280991 } } },
            { "name": "org.lwjgl:lwjgl:3.3.1:natives-linux",
              "rules": [ { "action": "allow" },
                         { "action": "disallow", "os": { "name": "osx" } } ],
              "downloads": { "artifact": { "path": "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-linux.jar",
                                           "sha1": "dd", "size": 86383 } } },
            { "name": "org.lwjgl:lwjgl:3.3.1:natives-macos-arm64",
              "rules": [ { "action": "allow", "os": { "arch": "arm-64", "name": "osx" } } ],
              "downloads": { "artifact": { "path": "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-macos-arm64.jar",
                                           "sha1": "ee", "size": 45773 } } }
          ],
          "arguments": {
            "game": [ "--username", "${auth_player_name}",
                      { "rules": [ { "action": "allow", "features": { "is_demo_user": true } } ],
                        "value": "--demo" },
                      { "rules": [ { "action": "allow", "os": { "name": "osx" } } ],
                        "value": [ "--startMaximized", "--fullscreen" ] } ],
            "jvm": [ "-p", "${classpath}",
                     { "rules": [ { "action": "allow", "os": { "name": "windows" } } ],
                       "value": "-XX:HeapDumpPath=MojangTricksIntelDriversForPerformance_javaw.exe" } ]
          }
        }
        """;

    [Fact]
    public void Parses_Modern_Version()
    {
        var info = VersionInfo.Parse(ModernVersion);

        Assert.Equal("1.20.1", info.Id);
        Assert.Equal("net.minecraft.client.main.Main", info.MainClass);
        Assert.Equal("12", info.Assets);
        Assert.Equal(17, info.RequiredJavaMajor);
        Assert.Equal("https://example.invalid/1.20.1-client.jar", info.Downloads?.Client?.Url);
        Assert.Equal(3, info.Libraries.Count);
    }

    [Fact]
    public void Parses_Nested_Arguments_Including_Groups()
    {
        var info = VersionInfo.Parse(ModernVersion);

        // game 数组：["--username", "${auth_player_name}", {rules:--demo}, {rules:[osx 组]}]
        Assert.Equal(4, info.Arguments?.Game.Count);
        Assert.Equal("--username", info.Arguments!.Game[0].Text);
        Assert.Equal("${auth_player_name}", info.Arguments.Game[1].Text);
        Assert.Null(info.Arguments.Game[0].Rules);
        Assert.NotNull(info.Arguments.Game[2].Rules);          // 带规则的 --demo
        Assert.Equal("--demo", info.Arguments.Game[2].Text);
        Assert.NotNull(info.Arguments.Game[3].Group);          // osx 的数组参数共享同一套规则
        Assert.Equal(2, info.Arguments.Game[3].Group!.Count);
    }

    [Fact]
    public void Legacy_Arguments_Are_Recognised()
    {
        var info = VersionInfo.Parse("""{"id":"1.8.9","minecraftArguments":"--username ${auth_player_name} --version ${version_name}"}""");

        Assert.Equal("--username ${auth_player_name} --version ${version_name}", info.MinecraftArguments);
        Assert.Null(info.Arguments);
    }

    [Fact]
    public void Inherit_Merges_Base_First_And_Fills_Gaps()
    {
        var baseVersion = VersionInfo.Parse(ModernVersion);
        var modLoader = VersionInfo.Parse("""
            { "id": "1.20.1-fabric", "inheritsFrom": "1.20.1",
              "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
              "libraries": [ { "name": "net.fabricmc:loader:0.15.0" } ],
              "arguments": { "game": [ "--launchTarget", "fabricclient" ] } }
            """);

        modLoader.InheritFrom(baseVersion);

        // mainClass 由 mod loader 覆盖，其余从原版补齐
        Assert.Equal("net.fabricmc.loader.impl.launch.knot.KnotClient", modLoader.MainClass);
        Assert.Equal("12", modLoader.Assets);
        Assert.Equal(17, modLoader.RequiredJavaMajor);
        Assert.Equal("https://example.invalid/1.20.1-client.jar", modLoader.Downloads?.Client?.Url);

        // 库：原版在前，mod loader 在后
        Assert.Equal(4, modLoader.Libraries.Count);
        Assert.Equal("net.fabricmc:loader:0.15.0", modLoader.Libraries[^1].Name);

        // 参数：原版 game 参数在前，附加参数在后 —— 顺序错了游戏会拒收参数
        Assert.Equal("--username", modLoader.Arguments!.Game[0].Text);
        Assert.Equal("fabricclient", modLoader.Arguments.Game[^1].Text);

        // 实际运行的 jar 仍是被继承的原版
        Assert.Equal("1.20.1", modLoader.ResolveJarName());
    }

    [Fact]
    public void Missing_Required_Java_Defaults_To_8()
    {
        Assert.Equal(8, VersionInfo.Parse("""{"id":"b1.8.8"}""").RequiredJavaMajor);
    }
}

public class RuleEvaluatorTests
{
    private static readonly RuntimeValue LinuxX64 = new("linux", "6.5.0", "x86");
    private static readonly RuntimeValue WindowsX64 = new("windows", "10.0", "x86");
    private static readonly RuntimeValue OsxX64 = new("osx", "23.1.0", "x86");
    private static readonly RuntimeValue OsxArm64 = new("osx", "23.1.0", "arm-64");

    [Fact]
    public void No_Rules_Means_Allowed()
    {
        Assert.True(RuleEvaluator.IsAllowed(null, LinuxX64));
        Assert.True(RuleEvaluator.IsAllowed([], LinuxX64));
    }

    [Fact]
    public void Unconditional_Allow_Then_Os_Disallow()
    {
        var rules = new List<Rule>
        {
            new("allow"),
            new("disallow", new OsRule(Name: "osx"))
        };

        Assert.True(RuleEvaluator.IsAllowed(rules, LinuxX64));
        Assert.False(RuleEvaluator.IsAllowed(rules, OsxX64));
    }

    [Fact]
    public void Last_Matching_Rule_Wins()
    {
        // 常见写法：先 allow，再对同一 OS allow 回来 —— 若实现成“任一 allow 即通过”，
        // 排在后面的 disallow 就会被忽略。
        var rules = new List<Rule>
        {
            new("allow"),
            new("disallow", new OsRule(Name: "linux")),
            new("allow", new OsRule(Name: "linux"))
        };

        Assert.True(RuleEvaluator.IsAllowed(rules, LinuxX64));
    }

    [Fact]
    public void Default_Is_Disallow_When_Rules_Exist()
    {
        var rules = new List<Rule> { new("allow", new OsRule(Name: "windows")) };

        Assert.True(RuleEvaluator.IsAllowed(rules, WindowsX64));
        Assert.False(RuleEvaluator.IsAllowed(rules, LinuxX64));
    }

    [Fact]
    public void Os_Version_Is_Matched_As_Regex()
    {
        var rules = new List<Rule> { new("allow", new OsRule(Name: "linux", Version: @"^3\.4.*")) };

        Assert.True(RuleEvaluator.IsAllowed(rules, new RuntimeValue("linux", "3.4.0", "x86")));
        Assert.False(RuleEvaluator.IsAllowed(rules, new RuntimeValue("linux", "6.5.0", "x86")));
    }

    [Fact]
    public void Arch_Supports_Alternation()
    {
        var rules = new List<Rule> { new("allow", new OsRule(Name: "osx", Arch: "x86|arm-64")) };

        Assert.True(RuleEvaluator.IsAllowed(rules, OsxX64));
        Assert.True(RuleEvaluator.IsAllowed(rules, OsxArm64));
        Assert.False(RuleEvaluator.IsAllowed(rules, LinuxX64));
    }

    [Fact]
    public void Sixty_Four_Bit_X86_Reports_As_X86()
    {
        // lwjgl 的 x86 专用原生库靠 arch=x86 规则把 Apple Silicon 排除掉。
        // 如果把 x64 映射成 "x64"，Intel Mac 会加载不到原生库、ARM Mac 反而会错加载。
        var runtime = RuntimeValue.ForCurrentPlatform(PlatformKind.MacOS, ArchitectureKind.X64);
        Assert.Equal("x86", runtime.OsArch);

        var arm = RuntimeValue.ForCurrentPlatform(PlatformKind.MacOS, ArchitectureKind.Arm64);
        Assert.Equal("arm-64", arm.OsArch);

        var rules = new List<Rule> { new("allow", new OsRule(Name: "osx", Arch: "x86")) };
        Assert.True(RuleEvaluator.IsAllowed(rules, runtime));
        Assert.False(RuleEvaluator.IsAllowed(rules, arm));
    }

    [Fact]
    public void Features_Gate_Demo_And_QuickPlay()
    {
        var rules = new List<Rule>
        {
            new("allow", Features: new Dictionary<string, bool> { ["is_demo_user"] = true })
        };

        Assert.True(RuleEvaluator.IsAllowed(rules,
            LinuxX64 with { Features = new HashSet<string>(StringComparer.Ordinal) { "is_demo_user" } }));
        Assert.False(RuleEvaluator.IsAllowed(rules, LinuxX64));
    }
}

public class MavenCoordinateTests
{
    [Theory]
    [InlineData("com.google.code.gson:gson:2.10.1", "com/google/code/gson/gson/2.10.1/gson-2.10.1.jar")]
    [InlineData("org.lwjgl:lwjgl:3.3.1:natives-linux", "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-linux.jar")]
    [InlineData("ca.weblite:java-objc-bridge:1.0.0.7", "ca/weblite/java-objc-bridge/1.0.0.7/java-objc-bridge-1.0.0.7.jar")]
    public void Derives_Repository_Path(string coordinate, string expectedPath)
    {
        var parsed = MavenCoordinate.Parse(coordinate);
        Assert.Equal(expectedPath, parsed.Path);
    }

    [Fact]
    public void Supports_Non_Jar_Extension()
    {
        var parsed = MavenCoordinate.Parse("com.mojang:patchy:1.3.9@zip");
        Assert.Equal("zip", parsed.Extension);
        Assert.Equal("com/mojang/patchy/1.3.9/patchy-1.3.9.zip", parsed.Path);
    }

    [Theory]
    [InlineData("g:a")]
    [InlineData("g:a:v:c:d")]
    [InlineData("")]
    public void Rejects_Malformed_Coordinate(string coordinate)
    {
        Assert.ThrowsAny<Exception>(() => MavenCoordinate.Parse(coordinate));
    }
}
