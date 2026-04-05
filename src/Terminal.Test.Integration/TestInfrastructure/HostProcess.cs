using System.Diagnostics;
using System.Text;

namespace Terminal.Test.Integration.TestInfrastructure;

/// <summary>
/// Represents a started ASP.NET Core host process that is controlled by the integration tests.
/// </summary>
internal sealed class HostProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _output = new();
    private readonly TaskCompletionSource<object?> _exitTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private HostProcess(Process process)
    {
        _process = process;
        _process.OutputDataReceived += OnOutputDataReceived;
        _process.ErrorDataReceived += OnOutputDataReceived;
        _process.Exited += (_, _) => _exitTcs.TrySetResult(null);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    /// <summary>Gets the executable label used in diagnostics.</summary>
    public string Name => _process.StartInfo.FileName;

    /// <summary>Gets the process identifier.</summary>
    public int ProcessId => _process.Id;

    /// <summary>
    /// Starts a managed child process with captured stdout and stderr.
    /// </summary>
    public static HostProcess Start(
        string fileName,
        string arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environmentVariables)
    {
        var processStartInfo = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var pair in environmentVariables)
        {
            processStartInfo.Environment[pair.Key] = pair.Value;
        }

        var process = new Process
        {
            StartInfo = processStartInfo,
            EnableRaisingEvents = true,
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process '{fileName} {arguments}'.");
        }

        return new HostProcess(process);
    }

    /// <summary>
    /// Throws when the host exits before the supplied readiness condition succeeds.
    /// </summary>
    public async Task WaitForReadyAsync(
        Func<CancellationToken, Task> readinessProbe,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string? diagnosticHint = null)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var timedOut = false;
        timeoutSource.Token.Register(() => timedOut = !cancellationToken.IsCancellationRequested);

        while (!timeoutSource.IsCancellationRequested)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Process '{_process.StartInfo.FileName}' exited before becoming ready.{Environment.NewLine}{diagnosticHint}{Environment.NewLine}{GetCapturedOutput()}");
            }

            try
            {
                await readinessProbe(timeoutSource.Token);
                return;
            }
            catch (Exception) when (!timeoutSource.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), timeoutSource.Token);
                }
                catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
                {
                    break;
                }
            }
        }

        if (timedOut)
        {
            throw new TimeoutException(
                $"Timed out waiting for process '{_process.StartInfo.FileName}' to become ready.{Environment.NewLine}{diagnosticHint}{Environment.NewLine}{GetCapturedOutput()}");
        }

        throw new TimeoutException(
            $"Timed out waiting for process '{_process.StartInfo.FileName}' to become ready.{Environment.NewLine}{diagnosticHint}{Environment.NewLine}{GetCapturedOutput()}");
    }

    /// <summary>
    /// Gets the captured process output for failure reporting.
    /// </summary>
    public string GetCapturedOutput()
    {
        lock (_output)
        {
            return _output.ToString();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _process.OutputDataReceived -= OnOutputDataReceived;
        _process.ErrorDataReceived -= OnOutputDataReceived;

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }

        using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        try
        {
            await _exitTcs.Task.WaitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
        }

        _process.Dispose();
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs eventArgs)
    {
        if (string.IsNullOrWhiteSpace(eventArgs.Data))
        {
            return;
        }

        lock (_output)
        {
            _output.AppendLine(eventArgs.Data);
        }
    }
}
