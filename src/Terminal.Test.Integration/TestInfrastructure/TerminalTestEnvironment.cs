using System.Net.Sockets;

namespace Terminal.Test.Integration.TestInfrastructure;

/// <summary>
/// Starts the mock identity host, the mock TN3270 host, and the API host for end-to-end integration tests.
/// </summary>
internal sealed class TerminalTestEnvironment : IAsyncDisposable
{
    private HostProcess? _mockServerProcess;
    private HostProcess? _apiProcess;

    private TerminalTestEnvironment(
        string repoRoot,
        int mockIdentityPort,
        int mockTerminalPort,
        int apiPort,
        string runId)
    {
        RepoRoot = repoRoot;
        MockIdentityPort = mockIdentityPort;
        MockTerminalPort = mockTerminalPort;
        ApiPort = apiPort;
        RunId = runId;
    }

    /// <summary>Gets the repository root used to locate built host assemblies.</summary>
    public string RepoRoot { get; }

    /// <summary>Gets the mock identity provider's HTTP port.</summary>
    public int MockIdentityPort { get; }

    /// <summary>Gets the mock TN3270 host's TCP port.</summary>
    public int MockTerminalPort { get; }

    /// <summary>Gets the API host's HTTP port.</summary>
    public int ApiPort { get; }

    /// <summary>Gets a unique identifier used to separate log files and in-memory database names.</summary>
    public string RunId { get; }

    /// <summary>Gets the mock issuer URL.</summary>
    public Uri MockIssuerUri => new($"http://127.0.0.1:{MockIdentityPort}/mock-entra/terminaltenant/v2.0", UriKind.Absolute);

    /// <summary>Gets the API HTTP base address.</summary>
    public Uri ApiBaseUri => new($"http://127.0.0.1:{ApiPort}", UriKind.Absolute);

    /// <summary>Gets the API terminal WebSocket URL.</summary>
    public Uri TerminalWebSocketUri => new($"ws://127.0.0.1:{ApiPort}/ws/terminal", UriKind.Absolute);

    /// <summary>
    /// Starts both hosts and waits for their readiness probes to succeed.
    /// </summary>
    public static async Task<TerminalTestEnvironment> StartAsync(CancellationToken cancellationToken = default)
    {
        var repoRoot = ResolveRepoRoot();
        var environment = new TerminalTestEnvironment(
            repoRoot,
            GetAvailableTcpPort(),
            GetAvailableTcpPort(),
            GetAvailableTcpPort(),
            Guid.NewGuid().ToString("N"));

        await environment.StartMockServerAsync(cancellationToken);
        await environment.StartApiAsync(cancellationToken);
        return environment;
    }

    /// <summary>
    /// Builds a unique path for host log files under <c>/tmp</c>.
    /// </summary>
    public string BuildLogPath(string hostName) =>
        Path.Combine("/tmp", $"terminal-{hostName}-{RunId}.log");

    /// <summary>
    /// Returns combined host output to improve failure messages.
    /// </summary>
    public string GetDiagnostics()
    {
        return string.Join(
            Environment.NewLine + Environment.NewLine,
            new[]
            {
                _mockServerProcess is null ? string.Empty : $"MockServer output:{Environment.NewLine}{_mockServerProcess.GetCapturedOutput()}",
                _apiProcess is null ? string.Empty : $"Terminal.Api output:{Environment.NewLine}{_apiProcess.GetCapturedOutput()}",
            }.Where(static section => !string.IsNullOrWhiteSpace(section)));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_apiProcess is not null)
        {
            await _apiProcess.DisposeAsync();
        }

        if (_mockServerProcess is not null)
        {
            await _mockServerProcess.DisposeAsync();
        }
    }

    private async Task StartMockServerAsync(CancellationToken cancellationToken)
    {
        var outputPath = ResolveHostAssemblyPath("Terminal.MockServer");
        var logPath = BuildLogPath("mockserver");
        var environmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{MockIdentityPort}",
            ["URLS"] = $"http://127.0.0.1:{MockIdentityPort}",
            ["Urls"] = $"http://127.0.0.1:{MockIdentityPort}",
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["MockIdentity__Issuer"] = MockIssuerUri.ToString().TrimEnd('/'),
            ["MockServer__Host"] = "127.0.0.1",
            ["MockServer__Port"] = MockTerminalPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["FileLogging__Path"] = logPath,
        };

        _mockServerProcess = HostProcess.Start(
            "dotnet",
            $"\"{outputPath}\"",
            Path.GetDirectoryName(outputPath)!,
            environmentVariables);

        await _mockServerProcess.WaitForReadyAsync(
            token => WaitForPortAsync(MockIdentityPort, token),
            timeout: ResolveStartupTimeout(),
            cancellationToken,
            diagnosticHint: $"MockServer log: {logPath}");
    }

    private async Task StartApiAsync(CancellationToken cancellationToken)
    {
        var outputPath = ResolveHostAssemblyPath("Terminal.Api");
        var logPath = BuildLogPath("api");
        var environmentVariables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{ApiPort}",
            ["URLS"] = $"http://127.0.0.1:{ApiPort}",
            ["Urls"] = $"http://127.0.0.1:{ApiPort}",
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["DOTNET_ENVIRONMENT"] = "Development",
            ["Authentication__Authority"] = MockIssuerUri.ToString().TrimEnd('/'),
            ["Tn3270E__Host"] = "127.0.0.1",
            ["Tn3270E__Port"] = MockTerminalPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["TerminalData__DatabaseName"] = $"TerminalApi-{RunId}",
            ["FileLogging__Path"] = logPath,
        };

        _apiProcess = HostProcess.Start(
            "dotnet",
            $"\"{outputPath}\"",
            Path.GetDirectoryName(outputPath)!,
            environmentVariables);

        await _apiProcess.WaitForReadyAsync(
            token => WaitForPortAsync(ApiPort, token),
            timeout: ResolveStartupTimeout(),
            cancellationToken,
            diagnosticHint: $"Terminal.Api log: {logPath}");
    }

    private string ResolveHostAssemblyPath(string hostProjectName)
    {
        var assemblyPath = Path.Combine(
            RepoRoot,
            "src",
            hostProjectName,
            "bin",
            "Debug",
            "net10.0",
            $"{hostProjectName}.dll");

        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException(
                $"Expected host assembly '{assemblyPath}' was not found. Build the solution before running integration tests.");
        }

        return assemblyPath;
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var solutionPath = Path.Combine(current.FullName, "src", "Terminal.slnx");
            if (File.Exists(solutionPath))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root containing src/Terminal.slnx.");
    }

    private static int GetAvailableTcpPort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task WaitForPortAsync(int port, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, port, cancellationToken);
    }

    private static TimeSpan ResolveStartupTimeout()
    {
        var configuredTimeout = Environment.GetEnvironmentVariable("TERMINAL_TEST_STARTUP_TIMEOUT_SECONDS");
        if (int.TryParse(configuredTimeout, out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.FromSeconds(60);
    }
}
