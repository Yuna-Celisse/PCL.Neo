using System.ComponentModel;
using PCL.Neo.Core.Platform;
using PCL.Neo.Core.Processes;

namespace PCL.Neo.Tests;

/// <summary>
/// 进程运行器是启动 Minecraft 的地基，三个平台的差异都在这里。
/// 用各平台必然存在的极小程序（echo / sleep / exit）做验证，不依赖 Java。
/// </summary>
public class ProcessRunnerTests
{
    private static (string FileName, string Arguments) EchoCommand(string text)
        => PlatformInfo.IsWindows
            ? ("cmd.exe", $"/c echo {text}")
            : ("/bin/echo", text);

    private static (string FileName, string Arguments) PrintEnvCommand(string variable)
        => PlatformInfo.IsWindows
            ? ("cmd.exe", $"/c echo %{variable}%")
            : ("/bin/sh", $"-c \"echo ${variable}\"");

    private static (string FileName, string Arguments) SleepCommand(int seconds)
        => PlatformInfo.IsWindows
            ? ("cmd.exe", $"/c ping -n {seconds + 1} 127.0.0.1 > nul")
            : ("/bin/sleep", seconds.ToString());

    private static (string FileName, string Arguments) ExitCodeCommand(int code)
        => PlatformInfo.IsWindows
            ? ("cmd.exe", $"/c exit {code}")
            : ("/bin/sh", $"-c \"exit {code}\"");

    [Fact]
    public async Task Captures_Standard_Output()
    {
        var runner = new ProcessRunner();
        var (file, args) = EchoCommand("pclneo-hello");

        using var process = await runner.StartAsync(new ProcessLaunchRequest(file, args));
        var exitCode = await process.WaitForExitAsync(CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains("pclneo-hello", process.StandardOutput.Trim());
    }

    [Fact]
    public async Task Reports_Non_Zero_Exit_Code()
    {
        var runner = new ProcessRunner();
        var (file, args) = ExitCodeCommand(3);

        using var process = await runner.StartAsync(new ProcessLaunchRequest(file, args));
        var exitCode = await process.WaitForExitAsync(CancellationToken.None);

        Assert.Equal(3, exitCode);
        Assert.True(process.HasExited);
    }

    [Fact]
    public async Task Applies_Environment_Overrides()
    {
        var runner = new ProcessRunner();
        const string variable = "PCL_NEO_TEST_MARKER";
        var (file, args) = PrintEnvCommand(variable);

        using var process = await runner.StartAsync(new ProcessLaunchRequest(
            file, args,
            Environment: new Dictionary<string, string?> { [variable] = "marker-value" }));

        await process.WaitForExitAsync(CancellationToken.None);

        Assert.Contains("marker-value", process.StandardOutput.Trim());
    }

    [Fact]
    public async Task Null_Environment_Value_Removes_Variable()
    {
        var runner = new ProcessRunner();
        // PATH 删掉后 cmd/sh 仍可执行内建命令，但子进程查询不到该变量
        const string variable = "PCL_NEO_REMOVED_MARKER";
        var (file, args) = PrintEnvCommand(variable);

        using var process = await runner.StartAsync(new ProcessLaunchRequest(
            file, args,
            Environment: new Dictionary<string, string?> { [variable] = null }));

        await process.WaitForExitAsync(CancellationToken.None);

        Assert.DoesNotContain("PCL_NEO_REMOVED_MARKER", process.StandardOutput.Trim());
    }

    [Fact]
    public async Task KillTree_Terminates_Long_Running_Process()
    {
        var runner = new ProcessRunner();
        var (file, args) = SleepCommand(30);

        using var process = await runner.StartAsync(new ProcessLaunchRequest(file, args));
        Assert.False(process.HasExited);

        process.KillTree();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync(timeout.Token);
        Assert.True(process.HasExited);
    }

    [Fact]
    public async Task Dispose_Kills_Process_That_Is_Still_Running()
    {
        var runner = new ProcessRunner();
        var (file, args) = SleepCommand(30);

        var process = await runner.StartAsync(new ProcessLaunchRequest(file, args));
        var pid = process.Pid;

        process.Dispose();

        // 从操作系统层面确认进程真的没了，而不是只看托管对象的标志位
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (!IsAlive(pid)) break;
            await Task.Delay(100);
        }

        Assert.False(IsAlive(pid), "Dispose 后进程仍在运行：托管释放没兜住泄漏，游戏进程会堆积");
    }

    /// <summary>直接向操作系统查询 PID 是否存活，绕开被测试对象自身。</summary>
    private static bool IsAlive(int pid)
    {
        try
        {
            using var probe = System.Diagnostics.Process.GetProcessById(pid);
            return !probe.HasExited;
        }
        catch (ArgumentException) { return false; }   // 已不存在
        catch (InvalidOperationException) { return false; }
    }

    [Fact]
    public async Task Missing_Executable_Fails_Through_The_Task_Not_Synchronously()
    {
        var runner = new ProcessRunner();
        var missing = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "pclneo-definitely-missing-" + Guid.NewGuid().ToString("N"));

        // 关键断言：调用本身不得抛出，异常必须出现在 await 处
        var pending = runner.StartAsync(new ProcessLaunchRequest(missing));

        await Assert.ThrowsAnyAsync<Exception>(async () => await pending);
    }

    [Fact]
    public void Empty_File_Name_Is_Rejected()
    {
        var runner = new ProcessRunner();

        // 参数错误按 .NET 约定同步抛出（属于调用方 bug，应立即失败），
        // 而运行时失败（可执行文件不存在）走 Task —— 见上一个用例。
        // 用块体 lambda 命中 Action 重载，才能真的断言“同步”抛出。
        Assert.Throws<ArgumentException>(() =>
        {
            _ = runner.StartAsync(new ProcessLaunchRequest(""));
        });
    }
}
