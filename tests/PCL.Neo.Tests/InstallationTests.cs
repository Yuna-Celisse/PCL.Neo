using System.Text;
using PCL.Neo.Core.Minecraft;
using PCL.Neo.Core.Minecraft.Manifest;

namespace PCL.Neo.Tests;

public class VersionChainTests
{
    private const string BaseVersion = """
        { "id": "1.20.1", "type": "release",
          "mainClass": "net.minecraft.client.main.Main",
          "assets": "12",
          "javaVersion": { "majorVersion": 17 },
          "libraries": [ { "name": "com.google.code.gson:gson:2.10.1" } ],
          "arguments": { "game": [ "--username", "${auth_player_name}" ] } }
        """;

    private const string ModLoader = """
        { "id": "1.20.1-fabric", "inheritsFrom": "1.20.1",
          "mainClass": "net.fabricmc.loader.impl.launch.knot.KnotClient",
          "libraries": [ { "name": "net.fabricmc:loader:0.15.0" } ],
          "arguments": { "game": [ "--launchTarget", "fabricclient" ] } }
        """;

    private static VersionManifestDocument ManifestWith(string id, string url) =>
        VersionManifestDocument.Parse(
            "{\"latest\":{\"release\":\"" + id + "\"},\"versions\":[{\"id\":\"" + id +
            "\",\"type\":\"release\",\"url\":\"" + url + "\"}]}");

    [Fact]
    public async Task Version_Without_Parent_Resolves_Without_Throwing()
    {
        // 没有 inheritsFrom 是常态；早期实现会在这里误抛异常
        await using var server = new TestHttpServer();
        server.AddFile("/v/1.20.1.json", BaseVersion);

        using var http = new HttpClient();
        var client = new MojangVersionClient(http, server.BaseUrl);

        var resolved = await client.ResolveChainAsync(
            "1.20.1", ManifestWith("1.20.1", server.BaseUrl + "/v/1.20.1.json"));

        Assert.Equal("1.20.1", resolved.Id);
        Assert.Equal("net.minecraft.client.main.Main", resolved.MainClass);
    }

    [Fact]
    public async Task Mod_Loader_Chain_Merges_From_Child_Upward()
    {
        await using var server = new TestHttpServer();
        server.AddFile("/v/1.20.1.json", BaseVersion);

        using var http = new HttpClient();
        var client = new MojangVersionClient(http, server.BaseUrl);

        var resolved = await client.ResolveChainAsync(
            "1.20.1-fabric",
            ManifestWith("1.20.1", server.BaseUrl + "/v/1.20.1.json"),
            localLookup: id => id == "1.20.1-fabric" ? VersionInfo.Parse(ModLoader) : null);

        // 方向反了的话：mainClass 会变成原版的，库顺序会颠倒，参数顺序也会颠倒
        Assert.Equal("1.20.1-fabric", resolved.Id);
        Assert.Equal("net.fabricmc.loader.impl.launch.knot.KnotClient", resolved.MainClass);
        Assert.Equal("12", resolved.Assets);
        Assert.Equal(17, resolved.RequiredJavaMajor);

        Assert.Equal(2, resolved.Libraries.Count);
        Assert.Equal("com.google.code.gson:gson:2.10.1", resolved.Libraries[0].Name);
        Assert.Equal("net.fabricmc:loader:0.15.0", resolved.Libraries[1].Name);

        Assert.Equal("--username", resolved.Arguments!.Game[0].Text);
        Assert.Equal("--launchTarget", resolved.Arguments.Game[2].Text);
    }

    [Fact]
    public async Task Detects_Inheritance_Cycle()
    {
        using var http = new HttpClient();
        var client = new MojangVersionClient(http, "http://127.0.0.1:1");

        var a = VersionInfo.Parse("""{"id":"a","inheritsFrom":"b"}""");
        var b = VersionInfo.Parse("""{"id":"b","inheritsFrom":"a"}""");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ResolveChainAsync("a", null, id => id switch { "a" => a, "b" => b, _ => null }));
    }

    [Fact]
    public async Task Throws_When_Version_Missing_From_Manifest()
    {
        using var http = new HttpClient();
        var client = new MojangVersionClient(http, "http://127.0.0.1:1");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.ResolveChainAsync("nope", VersionManifestDocument.Parse("""{"versions":[]}""")));
    }

    [Fact]
    public async Task Fetches_Version_List_From_Meta_Base()
    {
        await using var server = new TestHttpServer();
        server.AddFile("/mc/game/version_manifest_v2.json", """
            {"latest":{"release":"1.20.1","snapshot":"23w43a"},
             "versions":[
               {"id":"1.20.1","type":"release","url":"https://x/1.20.1.json","sha1":"aa"},
               {"id":"b1.8.8","type":"old_beta","url":"https://x/b1.8.8.json"}]}
            """);

        using var http = new HttpClient();
        var client = new MojangVersionClient(http, server.BaseUrl);
        var list = await client.GetVersionListAsync();

        Assert.Equal("1.20.1", list.Latest?.Release);
        Assert.Equal(2, list.Versions.Count);
        Assert.Single(list.FilterOfficial(includeSnapshots: false));
        Assert.Equal("1.20.1", list.Find("1.20.1")?.Id);
    }
}

public class InstallationPlannerTests
{
    private static readonly RuntimeValue LinuxX64 = new("linux", "6.5.0", "x86");
    private static readonly RuntimeValue OsxArm64 = new("osx", "23.1.0", "arm-64");

    private const string VersionJson = """
        { "id": "1.20.1", "mainClass": "net.minecraft.client.main.Main", "assets": "12",
          "javaVersion": { "majorVersion": 17 },
          "downloads": {
            "client": { "url": "https://origin/1.20.1.jar", "sha1": "c1", "size": 24000000 },
            "assetIndex": { "id": "12", "url": "https://origin/12.json", "sha1": "a1", "size": 400000 }
          },
          "libraries": [
            { "name": "com.google.code.gson:gson:2.10.1",
              "downloads": { "artifact": { "path": "com/google/code/gson/gson/2.10.1/gson-2.10.1.jar", "sha1": "g1", "size": 280991 } } },
            { "name": "org.lwjgl:lwjgl:3.3.1:natives-linux",
              "rules": [ { "action": "allow", "os": { "name": "linux" } } ],
              "downloads": { "artifact": { "path": "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-linux.jar", "sha1": "l1", "size": 86383 } } },
            { "name": "org.lwjgl:lwjgl:3.3.1:natives-macos-arm64",
              "rules": [ { "action": "allow", "os": { "arch": "arm-64", "name": "osx" } } ],
              "downloads": { "artifact": { "path": "org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-macos-arm64.jar", "sha1": "l2", "size": 45773 } } },
            { "name": "io.netty:netty-all:4.0.23.Final" }
          ] }
        """;

    private static AssetIndex SampleAssetIndex() => AssetIndex.Parse("""
        { "objects": {
            "icons/minecraft.icns": { "hash": "03c27cd7b0e6a4b7c8d9e0f1a2b3c4d5e6f70809", "size": 114190 },
            "pack.mcmeta": { "hash": "a1b2c3d4e5f60718293a4b5c6d7e8f9012345678", "size": 103 } } }
        """);

    [Fact]
    public void Builds_Downloads_With_Correct_Destinations_And_Hashes()
    {
        var version = VersionInfo.Parse(VersionJson);
        var layout = new MinecraftLayout("/mc");

        var plan = InstallationPlanner.Build(version, layout, LinuxX64, SampleAssetIndex());

        var byDestination = plan.Downloads.ToDictionary(d => d.Destination, StringComparer.Ordinal);

        Assert.True(byDestination.ContainsKey("/mc/versions/1.20.1/1.20.1.jar"));
        Assert.Equal("c1", byDestination["/mc/versions/1.20.1/1.20.1.jar"].Sha1);

        Assert.True(byDestination.ContainsKey("/mc/libraries/com/google/code/gson/gson/2.10.1/gson-2.10.1.jar"));
        Assert.True(byDestination.ContainsKey("/mc/libraries/org/lwjgl/lwjgl/3.3.1/lwjgl-3.3.1-natives-linux.jar"));

        // 无 downloads 的库按坐标推导，走默认仓库
        var netty = byDestination["/mc/libraries/io/netty/netty-all/4.0.23.Final/netty-all-4.0.23.Final.jar"];
        Assert.Equal("https://libraries.minecraft.net/io/netty/netty-all/4.0.23.Final/netty-all-4.0.23.Final.jar", netty.Url);
        Assert.Null(netty.Sha1);

        // 资源索引与对象
        Assert.True(byDestination.ContainsKey("/mc/assets/indexes/12.json"));
        Assert.True(byDestination.ContainsKey(
            "/mc/assets/objects/03/03c27cd7b0e6a4b7c8d9e0f1a2b3c4d5e6f70809"));
    }

    [Fact]
    public void Filters_Libraries_By_Platform_Rules()
    {
        var version = VersionInfo.Parse(VersionJson);
        var layout = new MinecraftLayout("/mc");

        var linux = InstallationPlanner.Build(version, layout, LinuxX64);
        var osx = InstallationPlanner.Build(version, layout, OsxArm64);

        Assert.Contains(linux.Downloads, d => d.Destination.EndsWith("natives-linux.jar", StringComparison.Ordinal));
        Assert.DoesNotContain(linux.Downloads, d => d.Destination.EndsWith("natives-macos-arm64.jar", StringComparison.Ordinal));

        Assert.Contains(osx.Downloads, d => d.Destination.EndsWith("natives-macos-arm64.jar", StringComparison.Ordinal));
        Assert.DoesNotContain(osx.Downloads, d => d.Destination.EndsWith("natives-linux.jar", StringComparison.Ordinal));
    }

    [Fact]
    public void Classpath_Is_Ordered_And_Deduplicated()
    {
        var version = VersionInfo.Parse(VersionJson);
        var layout = new MinecraftLayout("/mc");

        var plan = InstallationPlanner.Build(version, layout, LinuxX64);

        Assert.Equal(plan.Classpath.Count, plan.Classpath.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("/mc/libraries/com/google/code/gson/gson/2.10.1/gson-2.10.1.jar", plan.Classpath[0]);
        // gson + lwjgl-natives-linux + netty；osx 专用库被规则过滤掉
        Assert.Equal(3, plan.Classpath.Count);
    }

    [Fact]
    public void Asset_Objects_Use_Hash_As_Sha1()
    {
        var version = VersionInfo.Parse(VersionJson);
        var layout = new MinecraftLayout("/mc");

        var plan = InstallationPlanner.Build(version, layout, LinuxX64, SampleAssetIndex());
        var asset = plan.Downloads.First(d => d.Destination.Contains("/assets/objects/", StringComparison.Ordinal));

        Assert.Equal(Path.GetFileName(asset.Destination), asset.Sha1);
        Assert.StartsWith("https://resources.download.minecraft.net/", asset.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void TotalBytes_Sums_Only_Known_Sizes()
    {
        var version = VersionInfo.Parse(VersionJson);
        var layout = new MinecraftLayout("/mc");

        var plan = InstallationPlanner.Build(version, layout, LinuxX64, SampleAssetIndex());

        // 24000000 + 400000 + 280991 + 86383 + 114190 + 103（netty 无 size 不计）
        Assert.Equal(24000000 + 400000 + 280991 + 86383 + 114190 + 103, plan.TotalBytes);
    }

    [Fact]
    public void Missing_MainClass_Is_A_Hard_Error()
    {
        var version = VersionInfo.Parse("""{"id":"broken","libraries":[]}""");
        Assert.Throws<InvalidOperationException>(
            () => InstallationPlanner.Build(version, new MinecraftLayout("/mc"), LinuxX64));
    }

    [Fact]
    public void Layout_Paths_Are_Rooted_And_Separated()
    {
        var layout = new MinecraftLayout(Path.Combine(Path.GetTempPath(), "mc-root"));

        Assert.True(Path.IsPathRooted(layout.Root));
        Assert.EndsWith("versions", layout.VersionsDirectory);
        Assert.NotEqual(layout.LibrariesDirectory, layout.AssetsDirectory);
        Assert.Equal(
            Path.Combine(layout.Root, "versions", "1.20.1", "natives"),
            layout.NativesDirectory("1.20.1"));
    }
}
