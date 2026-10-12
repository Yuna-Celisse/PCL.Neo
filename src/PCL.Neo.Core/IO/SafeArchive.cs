using System.IO.Compression;

namespace PCL.Neo.Core.IO;

/// <summary>
/// 压缩包安全解包。
/// <para>
/// 启动器会解压来源不可信的档案：Mojang 库、Modrinth/CurseForge 模组、整合包。
/// PCL-CE 正在修的 <c>fix-dependency-auto-install-path-traversal-vulnerability</c>
/// 与 <c>propose-fix-for-library-path-vulnerability</c> 就是这类问题。
/// 因此这里把路径校验做成唯一入口，任何解压都必须经过它。
/// </para>
/// </summary>
public static class SafeArchive
{
    /// <summary>默认单文件解压上限（512 MiB），用于抑制 zip bomb。</summary>
    public const long DefaultMaxEntryBytes = 512L * 1024 * 1024;

    /// <summary>默认整包解压总量上限（2 GiB）。</summary>
    public const long DefaultMaxTotalBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// 把压缩包内的条目名解析为目标根目录下的安全绝对路径。
    /// </summary>
    /// <exception cref="IOException">条目试图逃出根目录（<c>..</c>）、为绝对路径或带盘符。</exception>
    public static string ResolveSecurePath(string destinationRoot, string entryName)
    {
        ArgumentException.ThrowIfNullOrEmpty(destinationRoot);
        ArgumentNullException.ThrowIfNull(entryName);

        // 归档可能由 Windows 工具制作，分隔符是反斜杠
        var normalized = entryName.Replace('\\', '/');

        if (Path.IsPathRooted(normalized) || normalized.Contains(":/", StringComparison.Ordinal))
            throw new IOException($"拒绝绝对路径条目：{entryName}");

        if (normalized.Length == 0)
            throw new IOException("空的归档条目名");

        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationRoot));
        var probe = new List<string>();

        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;

            if (segment == "..")
            {
                if (probe.Count == 0)
                    throw new IOException($"条目试图逃出目标目录：{entryName}");
                probe.RemoveAt(probe.Count - 1);
                continue;
            }

            if (ContainsInvalidCharacter(segment))
                throw new IOException($"条目名包含非法字符：{entryName}");

            probe.Add(segment);
        }

        if (probe.Count == 0)
            throw new IOException($"条目未指向任何文件：{entryName}");

        var relative = string.Join(Path.DirectorySeparatorChar, probe);
        var target = Path.GetFullPath(Path.Combine(rootFull, relative));

        // 规范化之后再确认一次前缀关系，覆盖符号链接之外的所有逃逸路径
        if (!target.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !string.Equals(target, rootFull, StringComparison.Ordinal))
        {
            throw new IOException($"条目解析后逃出目标目录：{entryName}");
        }

        return target;
    }

    /// <summary>
    /// 解压 zip 到指定目录，全程做路径穿越与容量校验。
    /// </summary>
    /// <remarks>
    /// 已知限制：<see cref="ZipArchiveEntry"/> 不公开 Unix 权限位，因此无法在此拦截
    /// 符号链接条目。若将来支持 tar/xz，必须换成能读取 mode 的实现。
    /// </remarks>
    public static long ExtractZip(
        string archivePath,
        string destinationRoot,
        bool overwrite = false,
        long maxEntryBytes = DefaultMaxEntryBytes,
        long maxTotalBytes = DefaultMaxTotalBytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(archivePath);
        ArgumentException.ThrowIfNullOrEmpty(destinationRoot);

        Directory.CreateDirectory(destinationRoot);

        long total = 0;

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            // 目录条目名以 / 结尾，跳过（CreateFromDirectory 也会写入它们）
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                var dir = ResolveSecurePath(destinationRoot, entry.FullName);
                Directory.CreateDirectory(dir);
                continue;
            }

            var target = ResolveSecurePath(destinationRoot, entry.FullName);

            if (entry.Length > maxEntryBytes)
                throw new IOException($"条目过大（{entry.Length} 字节）：{entry.FullName}");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            if (!overwrite && File.Exists(target))
                throw new IOException($"目标已存在：{target}");

            using var source = entry.Open();
            using var sink = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > maxTotalBytes)
                    throw new IOException("解压总量超过上限，疑似 zip bomb");
                sink.Write(buffer, 0, read);
            }
        }

        return total;
    }

    /// <summary>把一批文件打包成 zip（用于导出整合包与备份存档）。</summary>
    public static void CreateZip(string sourceDirectory, string archivePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceDirectory);
        ArgumentException.ThrowIfNullOrEmpty(archivePath);

        var directory = Path.GetDirectoryName(Path.GetFullPath(archivePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory!);

        if (File.Exists(archivePath)) File.Delete(archivePath);
        ZipFile.CreateFromDirectory(sourceDirectory, archivePath);
    }

    private static bool ContainsInvalidCharacter(string segment)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            if (segment.Contains(invalid)) return true;
        }

        return false;
    }
}
