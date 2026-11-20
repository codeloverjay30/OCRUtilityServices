using System.Diagnostics;
using System.IO.Abstractions;
using FluentAssertions;
using OCRUtilityServices.Paddle.Python;
using Xunit;

namespace OCRUtilityServices.IntegrationTests;

public sealed class Issue59PythonProcessRunnerTests : IDisposable
{
    private readonly IFileSystem _fileSystem = new FileSystem();
    private readonly string _directory;

    public Issue59PythonProcessRunnerTests()
    {
        _directory = _fileSystem.Path.Combine(
            _fileSystem.Path.GetTempPath(),
            "issue59-process-tests-" + Guid.NewGuid().ToString("N"));
        _fileSystem.Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task RunAsync_ValidProcess_ShouldReturnJson()
    {
        var runner = CreateRunner(
            "import json,sys\nprint(json.dumps({'schemaVersion':'1.0.0','image':sys.argv[-1]}))",
            TimeSpan.FromSeconds(15));

        string json = await runner.RunAsync("image with spaces.png", CancellationToken.None);

        json.Should().Contain("\"schemaVersion\": \"1.0.0\"");
        json.Should().Contain("image with spaces.png");
    }

    [Fact]
    public async Task RunAsync_NonZeroExit_ShouldIncludeExitCodeAndStderr()
    {
        var runner = CreateRunner(
            "import sys\nsys.stderr.write('issue59-child-failed\\n')\nsys.exit(23)",
            TimeSpan.FromSeconds(15));

        Func<Task> act = () => runner.RunAsync("input.png", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*code 23*issue59-child-failed*");
    }

    [Fact]
    public async Task RunAsync_Timeout_ShouldFailWithTimeoutException()
    {
        var runner = CreateRunner("import time\ntime.sleep(60)", TimeSpan.FromMilliseconds(500));

        Func<Task> act = () => runner.RunAsync("input.png", CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutException>()
            .WithMessage("*timed out*");
    }

    [Fact]
    public async Task RunAsync_Cancellation_ShouldPropagateCancellation()
    {
        var runner = CreateRunner("import time\ntime.sleep(60)", TimeSpan.FromSeconds(30));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        Func<Task> act = () => runner.RunAsync("input.png", cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunAsync_LargeStderrWithinLimit_ShouldNotDeadlock()
    {
        var runner = CreateRunner(
            "import sys\nsys.stderr.write('x'*12000)\nsys.stderr.flush()\nprint('{\"ok\":true}')",
            TimeSpan.FromSeconds(15));

        string result = await runner.RunAsync("input.png", CancellationToken.None);

        result.Trim().Should().Be("{\"ok\":true}");
    }

    [Fact]
    public async Task RunAsync_StdoutOverLimit_ShouldFailWithoutWaitingForNaturalExit()
    {
        var runner = CreateRunner(
            "import sys,time\nsys.stdout.write('x'*8192)\nsys.stdout.flush()\ntime.sleep(60)",
            TimeSpan.FromSeconds(10), maxOutputCharacters: 1024);
        var watch = Stopwatch.StartNew();

        Func<Task> act = () => runner.RunAsync("input.png", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*output exceeds the configured limit*");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task RunAsync_StderrOverLimit_ShouldFailWithoutWaitingForNaturalExit()
    {
        var runner = CreateRunner(
            "import sys,time\nsys.stderr.write('e'*20000)\nsys.stderr.flush()\ntime.sleep(60)",
            TimeSpan.FromSeconds(10));
        var watch = Stopwatch.StartNew();

        Func<Task> act = () => runner.RunAsync("input.png", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*output exceeds the configured limit*");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(8));
    }

    [Fact]
    public async Task RunAsync_EmptyStdout_ShouldRejectResult()
    {
        var runner = CreateRunner("pass", TimeSpan.FromSeconds(15));

        Func<Task> act = () => runner.RunAsync("input.png", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidDataException>()
            .WithMessage("*empty JSON*");
    }

    [Fact]
    public async Task RunAsync_PreCancelledToken_ShouldNotStartProcess()
    {
        var runner = CreateRunner("print('should not run')", TimeSpan.FromSeconds(15));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Func<Task> act = () => runner.RunAsync("input.png", cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private PythonOcrProcessRunner CreateRunner(
        string source, TimeSpan timeout, int maxOutputCharacters = 4_000_000)
    {
        string scriptPath = _fileSystem.Path.Combine(
            _directory, Guid.NewGuid().ToString("N") + ".py");
        _fileSystem.File.WriteAllText(scriptPath, source);
        string executable = Environment.GetEnvironmentVariable("ISSUE59_PYTHON_EXECUTABLE")
            ?? "python";
        return new PythonOcrProcessRunner(
            new PythonOcrProcessOptions(executable, scriptPath, timeout, maxOutputCharacters));
    }

    public void Dispose()
    {
        if (_fileSystem.Directory.Exists(_directory))
            _fileSystem.Directory.Delete(_directory, recursive: true);
    }
}
