using System.Text.Json;
using PCL.Neo.Core.Minecraft.Manifest;

namespace PCL.Neo.Core.Minecraft.Instances;

/// <summary>本地已存在的版本实例信息。</summary>
public sealed record LocalInstance(
    string Id,
    string DirectoryPath,
    string JsonPath,
    string? JarPath,
    VersionInfo? Version,
    DateTime LastModifiedUtc);

/// <summary>
/// 实例管理器：扫描本地 .minecraft/versions 目录下的版本。
/// </summary>
public sealed class InstanceManager(MinecraftLayout layout)
{
    private readonly MinecraftLayout _layout = layout ?? throw new ArgumentNullException(nameof(layout));

    public IReadOnlyList<LocalInstance> ScanInstances()
    {
        _layout.EnsureDirectories();
        var dir = _layout.VersionsDirectory;
        if (!Directory.Exists(dir)) return [];

        var list = new List<LocalInstance>();
        foreach (var subDir in Directory.GetDirectories(dir))
        {
            var id = Path.GetFileName(subDir);
            var jsonPath = _layout.VersionJsonPath(id);
            if (!File.Exists(jsonPath)) continue;

            VersionInfo? version = null;
            try
            {
                var text = File.ReadAllText(jsonPath);
                version = VersionInfo.Parse(text);
            }
            catch (JsonException)
            {
                // 损坏的 json，跳过或记录
            }

            var jarPath = _layout.VersionJarPath(id);
            var lastMod = Directory.GetLastWriteTimeUtc(subDir);

            list.Add(new LocalInstance(
                Id: id,
                DirectoryPath: subDir,
                JsonPath: jsonPath,
                JarPath: File.Exists(jarPath) ? jarPath : null,
                Version: version,
                LastModifiedUtc: lastMod));
        }

        return list.OrderByDescending(x => x.LastModifiedUtc).ToList();
    }
}
