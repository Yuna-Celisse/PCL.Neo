using System.Diagnostics;
using System.Text;

namespace PCL.Neo.Core.Processes;

/// <summary>启动一个外部进程所需的全部参数。</summary>
/// <param name="FileName">可执行文件路径。</param>
/// <param name="Arguments">原始参数字符串（由调用方负责引号，避免二次转义歧义）。</param>
/// <param name="WorkingDirectory">留空则使用当前目录。</param>
/// <param name="Environment">要追加或覆盖的环境变量；值为 null 表示删除该变量。</param>
/// <param name="RedirectStandardInput">Minecraft 需要向游戏进程 stdin 写入指令。</param>
/// <param name="OutputEncoding">
/// 标准输出/错误的解码方式。默认 UTF-8（Java 18+ 的 <c>file.encoding</c> 已是 UTF-8）。
/// 但 Java 8 在中文 Windows 上按系统 ANSI/OEM 代码页输出，强行用 UTF-8 会让中文日志乱码，
/// 因此调用方在驱动 Java 8 时必须显式传入对应代码页。
/// </param>
public sealed record ProcessLaunchRequest(
    string FileName,
    string Arguments = "",
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string?>? Environment = null,
    bool RedirectStandardInput = false,
    Encoding? OutputEncoding = null);

/// <summary>
/// 外部进程抽象。Minecraft 启动在三个平台上的差异（进程树终止、环境变量、
/// 控制台窗口、编码）全部收敛在这里，上层不得再直接 new <see cref="Process"/>。
/// </summary>
public interface IProcessRunner
{
    Task<ManagedProcess> StartAsync(ProcessLaunchRequest request, CancellationToken cancellationToken = default);
}

/// <summary>由 <see cref="IProcessRunner"/> 管理的运行中进程。</summary>
public sealed class ManagedProcess : IDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private readonly object _gate = new();
    private bool _disposed;

    internal ManagedProcess(Process process)
    {
        _process = process;
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (_gate) { _stdout.AppendLine(e.Data); }
            StdOutLine?.Invoke(e.Data);
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (_gate) { _stderr.AppendLine(e.Data); }
            StdErrLine?.Invoke(e.Data);
        };
    }

    /// <summary>逐行回调。游戏日志解析（崩溃分析、进度条）依赖这个。</summary>
    public event Action<string>? StdOutLine;
    public event Action<string>? StdErrLine;

    public int Pid => _process.Id;

    public string StandardOutput { get { lock (_gate) return _stdout.ToString(); } }
    public string StandardError { get { lock (_gate) return _stderr.ToString(); } }

    /// <summary>
    /// 等待退出并返回退出码。<see cref="System.Diagnostics.Process.WaitForExitAsync()"/>
    /// 返回的是非泛型 Task，因此退出码要在其后单独读取；.NET 会连带等待重定向流排空，
    /// 所以返回后 StandardOutput/StandardError 已完整。
    /// </summary>
    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return _process.ExitCode;
    }

    public bool HasExited => _process.HasExited;

    /// <summary>
    /// 终止整个进程树。<see cref="Process.Kill(bool)"/> 在 Unix 上会遍历 /proc 递归杀子进程，
    /// 在 Windows 上走工具快照；这是唯一跨平台可靠的树终止方式，游戏常会派生子进程。
    /// </summary>
    public void KillTree()
    {
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // 已在读取瞬间退出
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
        catch { /* 释放阶段吞掉异常 */ }
        _process.Dispose();
    }
}

public sealed class ProcessRunner : IProcessRunner
{
    public Task<ManagedProcess> StartAsync(ProcessLaunchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FileName))
            throw new ArgumentException("可执行文件路径不能为空", nameof(request));

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            Arguments = request.Arguments,
            WorkingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
                ? Environment.CurrentDirectory
                : request.WorkingDirectory!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = request.RedirectStandardInput,
            // 默认 UTF-8 而不是跟随系统区域设置：java -version 与各版本输出必须可解析。
            // 需要读 GBK 等代码页时由调用方通过 OutputEncoding 指定。
            StandardOutputEncoding = request.OutputEncoding ?? Encoding.UTF8,
            StandardErrorEncoding = request.OutputEncoding ?? Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (request.Environment is not null)
        {
            foreach (var (key, value) in request.Environment)
            {
                if (value is null) startInfo.Environment.Remove(key);
                else startInfo.Environment[key] = value;
            }
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        ManagedProcess? managed = new ManagedProcess(process);

        // 异步方法不得同步抛异常：所有失败都放进返回的 Task，
        // 否则调用方漏写 await 时异常会跑到完全意外的位置。
        try
        {
            if (!process.Start())
            {
                process.Dispose();
                return Task.FromException<ManagedProcess>(
                    new InvalidOperationException($"无法启动进程：{request.FileName}"));
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            var result = managed;
            managed = null;
            return Task.FromResult(result);
        }
        catch (Exception ex)
        {
            managed?.Dispose();
            return Task.FromException<ManagedProcess>(ex);
        }
    }
}
