using System.Diagnostics;
using System.Text;

namespace OCRUtilityServices.Paddle.Python;

/// <summary>Executes Python OCR with bounded streams, cancellation, and process-tree cleanup.</summary>
public sealed class PythonOcrProcessRunner : IPythonOcrProcessRunner
{
    private readonly PythonOcrProcessOptions _options;

    /// <summary>Validates the process configuration.</summary>
    public PythonOcrProcessRunner(PythonOcrProcessOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(options.PythonExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ScriptPath);
        if (options.Timeout <= TimeSpan.Zero ||
            options.MaxOutputCharacters is < 1024 or > 16_000_000)
            throw new ArgumentOutOfRangeException(nameof(options), "Invalid OCR process limits.");
    }

    /// <inheritdoc/>
    public async Task<string> RunAsync(string imagePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _options.PythonExecutable,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add(_options.ScriptPath);
        process.StartInfo.ArgumentList.Add("--image");
        process.StartInfo.ArgumentList.Add(imagePath);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(_options.Timeout);
        bool started = false;
        try
        {
            if (!process.Start())
                throw new InvalidOperationException("Python OCR process failed to start.");
            started = true;

            Task<string> stdout = ReadBoundedAsync(
                process.StandardOutput, _options.MaxOutputCharacters, linked.Token);
            Task<string> stderr = ReadBoundedAsync(
                process.StandardError, 16_384, linked.Token);
            Task exit = process.WaitForExitAsync(linked.Token);

            // Observe the first fault immediately: Task.WhenAll would otherwise
            // wait for a hung child after one redirected stream exceeds its limit.
            var pending = new List<Task> { stdout, stderr, exit };
            while (pending.Count != 0)
            {
                Task completed = await Task.WhenAny(pending).ConfigureAwait(false);
                await completed.ConfigureAwait(false);
                pending.Remove(completed);
            }

            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Python OCR exited with code {process.ExitCode}: {Sanitize(await stderr.ConfigureAwait(false))}");

            string json = await stdout.ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                throw new InvalidDataException("Python OCR returned empty JSON.");
            return json;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Python OCR process timed out.");
        }
        finally
        {
            if (started && !process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                try
                {
                    await process.WaitForExitAsync(CancellationToken.None)
                        .WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                }
                catch (TimeoutException) { }
            }
        }
    }

    /// <summary>Reads a redirected stream without retaining unbounded output.</summary>
    private static async Task<string> ReadBoundedAsync(
        StreamReader reader, int limit, CancellationToken token)
    {
        char[] buffer = new char[4096];
        var output = new StringBuilder();
        while (true)
        {
            int count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
            if (count == 0)
                return output.ToString();
            if (output.Length + count > limit)
                throw new InvalidDataException("Python OCR output exceeds the configured limit.");
            output.Append(buffer, 0, count);
        }
    }

    /// <summary>Limits multiline diagnostics to a single bounded message.</summary>
    private static string Sanitize(string value)
    {
        string normalized = value.Replace('\r', ' ').Replace('\n', ' ');
        return normalized[..Math.Min(normalized.Length, 1024)];
    }
}
