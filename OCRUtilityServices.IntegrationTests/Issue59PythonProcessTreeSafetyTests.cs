using System.Diagnostics;
using System.IO.Abstractions;
using FluentAssertions;
using OCRUtilityServices.Paddle.Python;
using Xunit;

namespace OCRUtilityServices.IntegrationTests;

public sealed class Issue59PythonProcessTreeSafetyTests : IDisposable
{
    private readonly IFileSystem _fileSystem = new FileSystem();
    private readonly string _directory;

    public Issue59PythonProcessTreeSafetyTests()
    {
        _directory = _fileSystem.Path.Combine(_fileSystem.Path.GetTempPath(),
            "issue59-tree-tests-" + Guid.NewGuid().ToString("N"));
        _fileSystem.Directory.CreateDirectory(_directory);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_Interrupted_ShouldTerminateDescendantProcess(bool cancel)
    {
        if (!OperatingSystem.IsWindows())
            return;

        string pidFile = _fileSystem.Path.Combine(_directory, "child.pid");
        string childScript = _fileSystem.Path.Combine(_directory, "child.py");
        string parentScript = _fileSystem.Path.Combine(_directory, "parent.py");
        _fileSystem.File.WriteAllText(childScript,
            "import os,sys,time\n" +
            "with open(sys.argv[1], 'w') as f:\n" +
            "    f.write(str(os.getpid()))\n" +
            "    f.flush()\n" +
            "time.sleep(60)\n");

        string executable = Environment.GetEnvironmentVariable("ISSUE59_PYTHON_EXECUTABLE") ?? "python";
        // The runner appends --image; this fixture deliberately ignores it.
        _fileSystem.File.WriteAllText(parentScript,
            "import subprocess,sys,time\n" +
            $"subprocess.Popen([sys.executable, {PythonLiteral(childScript)}, {PythonLiteral(pidFile)}])\n" +
            "time.sleep(60)\n");
        var runner = new PythonOcrProcessRunner(new PythonOcrProcessOptions(
            executable, parentScript, TimeSpan.FromSeconds(cancel ? 30 : 2)));
        using var cts = new CancellationTokenSource();
        Task<string> run = runner.RunAsync("unused.png", cts.Token);

        int? pid = null;
        try
        {
            using var readyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!_fileSystem.File.Exists(pidFile))
            {
                run.IsCompleted.Should().BeFalse("the descendant must start before interruption");
                await Task.Delay(40, readyTimeout.Token);
            }
            pid = int.Parse(_fileSystem.File.ReadAllText(pidFile),
                System.Globalization.CultureInfo.InvariantCulture);

            if (cancel)
                cts.Cancel();

            Func<Task> act = async () => await run;
            if (cancel)
                await act.Should().ThrowAsync<OperationCanceledException>();
            else
                await act.Should().ThrowAsync<TimeoutException>()
                    .WithMessage("*timed out*");

            bool exited = await WaitForExitAsync(pid.Value, TimeSpan.FromSeconds(5));
            exited.Should().BeTrue("interrupting OCR must terminate its descendant process");
        }
        finally
        {
            cts.Cancel();
            try { await run.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (Exception) { /* The original exception was asserted above. */ }

            if (pid is int childPid)
            {
                try
                {
                    using Process child = Process.GetProcessById(childPid);
                    if (!child.HasExited)
                    {
                        child.Kill(entireProcessTree: true);
                        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
                    }
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            }
        }
    }

    private static async Task<bool> WaitForExitAsync(int pid, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            try
            {
                using Process process = Process.GetProcessById(pid);
                if (process.HasExited)
                    return true;
            }
            catch (ArgumentException)
            {
                return true;
            }
            await Task.Delay(50);
        }
        return false;
    }

    private static string PythonLiteral(string value) =>
        "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    public void Dispose()
    {
        if (_fileSystem.Directory.Exists(_directory))
            _fileSystem.Directory.Delete(_directory, recursive: true);
    }
}
