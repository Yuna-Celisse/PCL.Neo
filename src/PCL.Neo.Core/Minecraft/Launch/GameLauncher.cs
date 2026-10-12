using System.Security.Cryptography;
using System.Text;
using PCL.Neo.Core.Minecraft.Java;
using PCL.Neo.Core.Minecraft.Manifest;
using PCL.Neo.Core.Platform;
using PCL.Neo.Core.Processes;

namespace PCL.Neo.Core.Minecraft.Launch;

/// <summary>启动 Minecraft 游戏时的入参要求。</summary>
public sealed record LaunchOptions(
    string JavaExecutable,
    int MaxMemoryMb = 2048,
    int MinMemoryMb = 512,
    string? CustomJvmArgs = null,
    string PlayerName = "Player",
    string? Uuid = null,
    string? AccessToken = null,
    string UserType = "msa",
    int WindowWidth = 854,
    int WindowHeight = 480);

/// <summary>
/// Minecraft 启动执行器。
/// 负责拼接参数、设置环境变量、并拉起目标游戏进程。
/// </summary>
public sealed class GameLauncher(IProcessRunner processRunner, MinecraftLayout layout)
{
    private readonly IProcessRunner _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    private readonly MinecraftLayout _layout = layout ?? throw new ArgumentNullException(nameof(layout));

    public async Task<ManagedProcess> LaunchAsync(
        VersionInfo version,
        LaunchOptions options,
        RuntimeValue runtime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runtime);

        if (string.IsNullOrEmpty(version.MainClass))
            throw new InvalidOperationException($"无法启动版本 {version.Id}：缺少 MainClass");

        if (!File.Exists(options.JavaExecutable))
            throw new FileNotFoundException($"找不到指定的 Java 运行时路径：{options.JavaExecutable}");

        // 1. 组装 classpath：版本专属 client.jar + 全部符合规则的依赖库
        var clientJar = _layout.VersionJarPath(version.ResolveJarName());
        var classpathElements = new List<string>();

        foreach (var lib in version.Libraries)
        {
            if (!lib.IsAllowed(runtime)) continue;
            foreach (var artifact in lib.ResolveArtifacts(runtime))
            {
                var p = _layout.LibraryPath(artifact.RelativePath);
                if (File.Exists(p) && !classpathElements.Contains(p))
                    classpathElements.Add(p);
            }
        }

        if (File.Exists(clientJar) && !classpathElements.Contains(clientJar))
            classpathElements.Add(clientJar);

        // 2. 准备变量表
        var uuid = string.IsNullOrEmpty(options.Uuid) ? GenerateOfflineUuid(options.PlayerName) : options.Uuid;
        var token = options.AccessToken ?? "0";
        var nativesDir = _layout.NativesDirectory(version.Id);

        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["auth_player_name"] = options.PlayerName,
            ["version_name"] = version.Id,
            ["game_directory"] = _layout.Root,
            ["assets_root"] = _layout.AssetsDirectory,
            ["game_assets"] = _layout.AssetsDirectory,
            ["assets_index_name"] = version.Assets ?? "legacy",
            ["auth_uuid"] = uuid,
            ["auth_access_token"] = token,
            ["user_type"] = options.UserType,
            ["version_type"] = version.Type ?? "release",
            ["user_properties"] = "{}",
            ["natives_directory"] = nativesDir,
            ["launcher_name"] = "PCL.Neo",
            ["launcher_version"] = "1.0.0",
            ["resolution_width"] = options.WindowWidth.ToString(),
            ["resolution_height"] = options.WindowHeight.ToString()
        };

        // 3. 组装 JVM 参数
        var jvmArgs = new List<string>
        {
            $"-Xms{options.MinMemoryMb}M",
            $"-Xmx{options.MaxMemoryMb}M"
        };

        if (PlatformInfo.IsMacOS)
        {
            jvmArgs.Add("-XstartOnFirstThread");
        }

        if (!string.IsNullOrWhiteSpace(options.CustomJvmArgs))
        {
            jvmArgs.AddRange(options.CustomJvmArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        // 清单自带的 JVM 参数 (例如 classpath、natives 路径等)
        var manifestJvmArgs = LaunchArguments.BuildJvmArguments(
            version.Arguments,
            runtime,
            variables,
            classpathElements,
            strictPlaceholders: false);
        jvmArgs.AddRange(manifestJvmArgs);

        // 4. 组装 Game 参数
        var gameArgs = new List<string>();
        if (version.Arguments is not null && version.Arguments.Game.Count > 0)
        {
            gameArgs.AddRange(LaunchArguments.Resolve(version.Arguments.Game, runtime, variables, strictPlaceholders: false));
        }
        else
        {
            gameArgs.AddRange(LaunchArguments.ResolveLegacy(version.MinecraftArguments, variables, strictPlaceholders: false));
        }

        // 5. 拼接全部命令行参数并启动进程
        var allArgs = new List<string>(jvmArgs.Count + 1 + gameArgs.Count);
        allArgs.AddRange(jvmArgs);
        allArgs.Add(version.MainClass);
        allArgs.AddRange(gameArgs);

        var argumentsString = EscapeAndJoinArguments(allArgs);

        var request = new ProcessLaunchRequest(
            FileName: options.JavaExecutable,
            Arguments: argumentsString,
            WorkingDirectory: _layout.Root);

        return await _processRunner.StartAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>生成符合 Mojang 离线 UUID 规范的离线玩家 UUID (UUID v3, 大端序)。</summary>
    public static string GenerateOfflineUuid(string playerName)
    {
        var input = Encoding.UTF8.GetBytes("OfflinePlayer:" + playerName);
        var hash = MD5.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0f) | 0x30); // version 3
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80); // IETF variant
        // .NET 的 new Guid(byte[]) 期望小端序 (a, b, c 字段)，若直接传入会导致字节翻转，
        // 因此构造时指定 bigEndian: true 以确保标准 UUID 规范输出。
        return new Guid(hash, bigEndian: true).ToString("D");
    }

    private static string EscapeAndJoinArguments(IEnumerable<string> args)
    {
        var sb = new StringBuilder();
        foreach (var arg in args)
        {
            if (sb.Length > 0) sb.Append(' ');
            if (arg.Contains(' ') || arg.Contains('\t') || arg.Contains('"'))
            {
                sb.Append('"').Append(arg.Replace("\"", "\\\"")).Append('"');
            }
            else
            {
                sb.Append(arg);
            }
        }
        return sb.ToString();
    }
}
