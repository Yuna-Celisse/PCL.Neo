using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using PCL.Neo.Core.IO.Downloads;
using PCL.Neo.Core.Minecraft;
using PCL.Neo.Core.Minecraft.Installer;
using PCL.Neo.Core.Minecraft.Launch;
using PCL.Neo.Core.Minecraft.Manifest;
using PCL.Neo.Core.Platform;
using PCL.Neo.Core.Processes;

namespace PCL.Neo.Tests;

public class InstallerAndLauncherTests
{
    private static readonly RuntimeValue LinuxX64 = new("linux", "6.5.0", "x86");

    private static string Sha1(byte[] data) => Convert.ToHexString(SHA1.HashData(data)).ToLowerInvariant();

    private static byte[] CreateZipWithFile(string fileName, string content)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var entry = zip.CreateEntry(fileName);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }
        return ms.ToArray();
    }

    [Fact]
    public async Task GameInstaller_Installs_Files_And_Extracts_Natives()
    {
        await using var server = new TestHttpServer();
        using var temp = TempDirectory.Create();
        var layout = new MinecraftLayout(temp.Root);

        // 构造虚拟 native jar 和 client jar
        var nativeContent = "libdummy.so binary data";
        var nativeZipBytes = CreateZipWithFile("libdummy.so", nativeContent);
        var clientBytes = Encoding.UTF8.GetBytes("fake client jar");
        var assetIndexBytes = Encoding.UTF8.GetBytes("""{"objects":{}}""");

        server.AddFile("/mc/game/client.jar", clientBytes);
        server.AddFile("/mc/libs/native.jar", nativeZipBytes);
        server.AddFile("/mc/assets/indexes/1.20.1.json", assetIndexBytes);

        var versionJson = $$"""
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "assets": "1.20.1",
          "downloads": {
            "client": { "url": "{{server.BaseUrl}}/mc/game/client.jar", "sha1": "{{Sha1(clientBytes)}}", "size": {{clientBytes.Length}} },
            "assetIndex": { "id": "1.20.1", "url": "{{server.BaseUrl}}/mc/assets/indexes/1.20.1.json", "sha1": "{{Sha1(assetIndexBytes)}}", "size": {{assetIndexBytes.Length}} }
          },
          "libraries": [
            {
              "name": "org.lwjgl:lwjgl-platform:3.3.1:natives-linux",
              "rules": [ { "action": "allow", "os": { "name": "linux" } } ],
              "natives": { "linux": "natives-linux" },
              "downloads": {
                "classifiers": {
                  "natives-linux": {
                    "path": "org/lwjgl/lwjgl-platform/3.3.1/lwjgl-platform-3.3.1-natives-linux.jar",
                    "url": "{{server.BaseUrl}}/mc/libs/native.jar",
                    "sha1": "{{Sha1(nativeZipBytes)}}",
                    "size": {{nativeZipBytes.Length}}
                  }
                }
              }
            }
          ]
        }
        """;

        server.AddFile("/v/1.20.1.json", versionJson);
        var manifest = VersionManifestDocument.Parse($$"""
        {
          "latest": { "release": "1.20.1" },
          "versions": [ { "id": "1.20.1", "type": "release", "url": "{{server.BaseUrl}}/v/1.20.1.json" } ]
        }
        """);

        using var http = new HttpClient();
        var client = new MojangVersionClient(http, server.BaseUrl);
        var downloader = new FileDownloader(http);
        var installer = new GameInstaller(client, downloader, layout);

        var stages = new List<InstallationStage>();
        var progress = new Progress<InstallationProgress>(p => stages.Add(p.Stage));

        var plan = await installer.InstallAsync("1.20.1", manifest, LinuxX64, progress);

        Assert.Equal("1.20.1", plan.VersionId);
        Assert.True(File.Exists(layout.VersionJarPath("1.20.1")));
        Assert.True(File.Exists(layout.AssetIndexPath("1.20.1")));

        // 验证 Native 是否被解压出来
        var extractedNativePath = Path.Combine(layout.NativesDirectory("1.20.1"), "libdummy.so");
        Assert.True(File.Exists(extractedNativePath));
        Assert.Equal(nativeContent, File.ReadAllText(extractedNativePath));
    }

    [Fact]
    public async Task GameLauncher_Assembles_Correct_Command_And_Starts_Process()
    {
        using var temp = TempDirectory.Create();
        var layout = new MinecraftLayout(temp.Root);
        layout.EnsureDirectories();

        var clientJar = layout.VersionJarPath("1.20.1");
        Directory.CreateDirectory(Path.GetDirectoryName(clientJar)!);
        File.WriteAllText(clientJar, "dummy jar");

        var version = VersionInfo.Parse("""
        {
          "id": "1.20.1",
          "mainClass": "net.minecraft.client.main.Main",
          "arguments": {
            "game": [ "--username", "${auth_player_name}", "--version", "${version_name}" ],
            "jvm": [ "-Djava.library.path=${natives_directory}", "-cp", "${classpath}" ]
          }
        }
        """);

        // 找一个当前平台上一定存在的测试可执行程序 (如 echo 或 cmd)
        var dummyJava = temp.PathOf(PlatformInfo.IsWindows ? "mock_java.bat" : "mock_java.sh");
        if (PlatformInfo.IsWindows)
        {
            File.WriteAllText(dummyJava, "@echo off\r\necho %*\r\n");
        }
        else
        {
            File.WriteAllText(dummyJava, "#!/bin/sh\necho \"$@\"\n");
            // 赋予执行权限 (加 OSPlatform 守卫避免分析器报警 CA1416)
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                File.SetUnixFileMode(dummyJava, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        var runner = new ProcessRunner();
        var launcher = new GameLauncher(runner, layout);

        var options = new LaunchOptions(
            JavaExecutable: dummyJava,
            PlayerName: "Steve",
            MaxMemoryMb: 1024,
            MinMemoryMb: 256);

        using var process = await launcher.LaunchAsync(version, options, LinuxX64);
        var exitCode = await process.WaitForExitAsync();

        Assert.Equal(0, exitCode);
        var output = process.StandardOutput;

        Assert.Contains("net.minecraft.client.main.Main", output);
        Assert.Contains("-Xms256M", output);
        Assert.Contains("-Xmx1024M", output);
        Assert.Contains("--username Steve", output);
        Assert.Contains("--version 1.20.1", output);
    }

    [Fact]
    public void GenerateOfflineUuid_Produces_Deterministic_V3_Uuid()
    {
        var uuid1 = GameLauncher.GenerateOfflineUuid("Steve");
        var uuid2 = GameLauncher.GenerateOfflineUuid("Steve");
        var alexUuid = GameLauncher.GenerateOfflineUuid("Alex");

        Assert.Equal(uuid1, uuid2);
        Assert.NotEqual(uuid1, alexUuid);
        // 标准 UUID 格式：8-4-4-4-12
        Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", uuid1);
        // 版本号必须是 3
        Assert.Equal('3', uuid1[14]);
    }
}
