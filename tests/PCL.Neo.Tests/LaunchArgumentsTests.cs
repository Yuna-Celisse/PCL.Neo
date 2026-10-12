using PCL.Neo.Core.Minecraft;
using PCL.Neo.Core.Minecraft.Manifest;

namespace PCL.Neo.Tests;

public class LaunchArgumentsTests
{
    private static readonly RuntimeValue Linux = new("linux", "6.5.0", "x86");

    private static Dictionary<string, string> Vars(params (string Key, string Value)[] extra)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_player_name"] = "Steve",
            ["version_name"] = "1.20.1",
            ["game_directory"] = "/home/u/.minecraft",
            ["auth_uuid"] = "0-0",
            ["auth_access_token"] = "token",
            ["user_type"] = "msa",
            ["version_type"] = "release",
            ["user_properties"] = "{}",
            ["assets_index_name"] = "12",
            ["assets_root"] = "/home/u/.minecraft/assets",
            ["game_assets"] = "/home/u/.minecraft/assets/virtual/legacy"
        };
        foreach (var (k, v) in extra) d[k] = v;
        return d;
    }

    [Fact]
    public void Expands_Placeholders()
    {
        var args = LaunchArguments.Resolve(
            [new Argument { Text = "--username" }, new Argument { Text = "${auth_player_name}" }],
            Linux, Vars());

        Assert.Equal(["--username", "Steve"], args);
    }

    [Fact]
    public void Drops_Arguments_That_Expand_To_Empty()
    {
        // 离线模式下 auth_access_token 为空。若保留成空参数，
        // 后面的位置参数会整体错位，游戏以难以排查的方式失败。
        var args = LaunchArguments.Resolve(
            [
                new Argument { Text = "--accessToken" },
                new Argument { Text = "${auth_access_token}" },
                new Argument { Text = "--version" },
                new Argument { Text = "${version_name}" }
            ],
            Linux, Vars(("auth_access_token", "")));

        Assert.Equal(["--accessToken", "--version", "1.20.1"], args);
    }

    [Fact]
    public void Skips_Rule_Gated_Arguments_That_Do_Not_Match()
    {
        var args = LaunchArguments.Resolve(
            [
                new Argument { Text = "--demo", Rules = [new Rule("allow", new OsRule(Name: "windows"))] },
                new Argument { Text = "--linux-only", Rules = [new Rule("allow", new OsRule(Name: "linux"))] }
            ],
            Linux, Vars());

        Assert.Equal(["--linux-only"], args);
    }

    [Fact]
    public void Group_Shared_Rules_Expand_In_Order()
    {
        var args = LaunchArguments.Resolve(
            [
                new Argument
                {
                    Rules = [new Rule("allow", new OsRule(Name: "osx"))],
                    Group = [new Argument { Text = "--startMaximized" }, new Argument { Text = "--fullscreen" }]
                },
                new Argument
                {
                    Rules = [new Rule("allow", new OsRule(Name: "linux"))],
                    Group = [new Argument { Text = "--lwjgl" }, new Argument { Text = "3" }]
                }
            ],
            Linux, Vars());

        Assert.Equal(["--lwjgl", "3"], args);
    }

    [Fact]
    public void Legacy_String_Arguments_Are_Split_And_Expanded()
    {
        var args = LaunchArguments.ResolveLegacy(
            "--username ${auth_player_name} --version ${version_name}", Vars());

        Assert.Equal(["--username", "Steve", "--version", "1.20.1"], args);
    }

    [Fact]
    public void Unknown_Placeholder_Throws_In_Strict_Mode()
    {
        Assert.Throws<KeyNotFoundException>(
            () => LaunchArguments.Expand("${nope}", Vars(), strict: true));

        Assert.Equal("${nope}", LaunchArguments.Expand("${nope}", Vars(), strict: false));
    }

    [Fact]
    public void Jvm_Arguments_Inject_Classpath_With_Platform_Separator()
    {
        var spec = new ArgumentsSpec();
        spec.Jvm.Add(new Argument { Text = "-p" });
        spec.Jvm.Add(new Argument { Text = "${classpath}" });

        var args = LaunchArguments.BuildJvmArguments(spec, Linux, Vars(), ["/a.jar", "/b.jar"]);

        Assert.Equal("-p", args[0]);
        Assert.Equal($"/a.jar{Path.PathSeparator}/b.jar", args[1]);
    }

    [Fact]
    public void Pre_Modern_Minecraft_Falls_Back_To_Direct_Classpath()
    {
        // 1.12 及更早的清单没有 jvm 参数，必须回落到经典写法
        var args = LaunchArguments.BuildJvmArguments(null, Linux, Vars(), ["/a.jar", "/b.jar"]);

        Assert.Single(args);
        Assert.Equal($"-Djava.class.path=/a.jar{Path.PathSeparator}/b.jar", args[0]);
    }
}
