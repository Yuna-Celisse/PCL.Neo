using System.Globalization;
using System.Text;

namespace PCL.Neo.Core.Configuration;

/// <summary>一次配置结构迁移。</summary>
/// <param name="TargetVersion">应用后达到的版本。</param>
/// <param name="Apply">就地修改文档的委托。</param>
public sealed record ConfigMigration(int TargetVersion, Action<IniDocument> Apply);

/// <summary>
/// 内置迁移表。新增配置结构变更时，在末尾追加一条并把 <see cref="LatestVersion"/> 递增；
/// <b>永远不要修改或删除已发布的历史迁移</b>，否则老用户的配置无法追上。
/// </summary>
public static class ConfigMigrations
{
    public static IReadOnlyList<ConfigMigration> BuiltIn { get; } = [];

    public static int LatestVersion { get; } =
        BuiltIn.Count == 0 ? InitialVersion : BuiltIn[^1].TargetVersion;

    /// <summary>新配置文件的起始版本。</summary>
    public const int InitialVersion = 1;
}

/// <summary>
/// 类型化配置存储：内存模型 + 脏标记 + 原子落盘。
/// <para>
/// 原子写：先写同目录临时文件，再 <see cref="File.Replace"/> / Move 覆盖目标。
/// 启动器崩溃或断电时不会留下半截配置——这是启动器最致命的故障模式，
/// 因为损坏的配置文件会让用户连游戏列表都打不开。
/// </para>
/// </summary>
public sealed class SettingsStore
{
    public const string MetaSection = "meta";
    public const string VersionKey = "Version";

    private readonly IniDocument _document;
    private bool _dirty;

    private SettingsStore(string filePath, string section, IniDocument document)
    {
        FilePath = filePath;
        Section = section;
        _document = document;
    }

    public string FilePath { get; }

    /// <summary>本实例读写的节名。</summary>
    public string Section { get; }

    /// <summary>键变更通知（参数为键名）。UI 绑定用它做即时刷新。</summary>
    public event Action<string>? SettingChanged;

    public int Version => GetInt(VersionKey, ConfigMigrations.InitialVersion, MetaSection);

    private static int ReadVersion(IniDocument document)
        => document.TryGetValue(MetaSection, VersionKey, out var raw)
           && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var version)
            ? version
            : ConfigMigrations.InitialVersion;

    public bool IsDirty { get { lock (_gate) return _dirty; } }

    /// <summary>读取或创建配置文件，并自动升级到最新版本。</summary>
    public static SettingsStore Load(
        string filePath,
        string section = "main",
        IReadOnlyList<ConfigMigration>? migrations = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        migrations ??= ConfigMigrations.BuiltIn;
        IniDocument document;
        var versionBefore = ConfigMigrations.InitialVersion;

        if (File.Exists(filePath))
        {
            // UTF-8 读取；BOM 会被 StreamReader 自动剥离
            document = IniDocument.Parse(File.ReadAllText(filePath, Encoding.UTF8));
            versionBefore = ReadVersion(document);
        }
        else
        {
            document = new IniDocument();
        }

        Migrate(document, migrations);
        var versionAfter = ReadVersion(document);

        var store = new SettingsStore(filePath, section, document);

        // 迁移结果必须立刻落盘，否则每次启动都会把同一批迁移重跑一遍。
        // 覆盖前先留一份 .bak：迁移写坏配置会让用户连实例列表都打不开，必须可回滚。
        if (versionAfter != versionBefore && File.Exists(filePath))
        {
            try
            {
                File.Copy(filePath, filePath + ".bak", overwrite: true);
            }
            catch (IOException)
            {
                // 备份失败不阻断升级，但也不能因此丢掉迁移结果
            }

            store._dirty = true;
            store.Save();
        }

        return store;
    }

    /// <summary>按迁移表就地升级文档。可从空文档调用。</summary>
    public static void Migrate(IniDocument document, IReadOnlyList<ConfigMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(migrations);

        // 没有版本戳的文件视为初版：可能是手工创建的，也可能是迁移机制之前的老文件
        var current = document.TryGetValue(MetaSection, VersionKey, out var raw)
                      && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : ConfigMigrations.InitialVersion;

        foreach (var migration in migrations)
        {
            if (migration.TargetVersion <= current) continue;
            migration.Apply(document);
            current = migration.TargetVersion;
        }

        document.SetValue(MetaSection, VersionKey, current.ToString(CultureInfo.InvariantCulture));
    }

    public string GetString(string key, string fallback = "", string? section = null)
    {
        lock (_gate)
            return _document.TryGetValue(section ?? Section, key, out var value) ? value : fallback;
    }

    public void SetString(string key, string value, string? section = null)
    {
        lock (_gate)
        {
            var target = section ?? Section;
            if (_document.TryGetValue(target, key, out var existing)
                && string.Equals(existing, value, StringComparison.Ordinal))
                return;

            _document.SetValue(target, key, value);
            _dirty = true;
            SettingChanged?.Invoke(key);
        }
    }

    public int GetInt(string key, int fallback = 0, string? section = null)
        => int.TryParse(GetString(key, "", section), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : fallback;

    public void SetInt(string key, int value, string? section = null)
        => SetString(key, value.ToString(CultureInfo.InvariantCulture), section);

    /// <summary>接受 true/false（大小写不敏感）；其它值一律回退到 fallback。</summary>
    public bool GetBool(string key, bool fallback = false, string? section = null)
        => bool.TryParse(GetString(key, "", section), out var value) ? value : fallback;

    public void SetBool(string key, bool value, string? section = null)
        => SetString(key, value ? "true" : "false", section);

    public bool Remove(string key, string? section = null)
    {
        lock (_gate)
        {
            if (!_document.Remove(section ?? Section, key)) return false;
            _dirty = true;
            SettingChanged?.Invoke(key);
            return true;
        }
    }

    /// <summary>串行化对文档与磁盘的访问：List 本身不是线程安全的。</summary>
    private readonly object _gate = new();

    /// <summary>
    /// 有改动时原子写盘。返回是否实际写入。
    /// <para>
    /// 两个必须处理的并发问题：
    /// 1) 同一实例被多个后台线程调用 —— 用 <c>_saveGate</c> 串行化，
    ///    否则两个线程会争抢同一个临时文件；
    /// 2) 临时文件名必须唯一 —— 固定名会让并发写互相覆盖，
    ///    甚至在 Replace 之后被另一个线程的 Move 撞上 FileNotFoundException。
    ///    （这两点都由 Concurrent_Saves_Never_Produce_Corrupt_File 用例暴露。）
    /// </para>
    /// <para>
    /// 跨进程并发（同时开两个启动器）这里只做到“不产生半截文件”，
    /// 最后写入者胜出；真正的多实例保护要靠单实例锁，属于后续里程碑。
    /// </para>
    /// </summary>
    public bool Save()
    {
        lock (_gate)
        {
            if (!_dirty) return false;

            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory!);

            var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, _document.ToText(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                try
                {
                    // File.Replace 在 Unix 上由 rename 实现，同样是原子的；
                    // 目标不存在时会抛 FileNotFoundException，此时退化为 Move。
                    File.Replace(temp, FilePath, destinationBackupFileName: null);
                }
                catch (FileNotFoundException)
                {
                    File.Move(temp, FilePath, overwrite: true);
                }
                catch (IOException)
                {
                    // 目标被占用（例如另一个启动器实例）：直接覆盖，不丢数据
                    File.Move(temp, FilePath, overwrite: true);
                }

                _dirty = false;
                return true;
            }
            finally
            {
                // 失败路径下不留垃圾文件
                if (File.Exists(temp))
                {
                    try { File.Delete(temp); } catch (IOException) { }
                }
            }
        }
    }
}
