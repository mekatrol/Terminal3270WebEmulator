using System.Collections.Concurrent;
using System.Diagnostics;
using Terminal.Test.Integration.TestInfrastructure;

namespace Terminal.Test.Integration.Load;

/// <summary>
/// Runs end-to-end load through the mock identity provider, the API WebSocket proxy, and the mock TN3270 host.
/// </summary>
[TestClass]
public sealed class TerminalLoadTests
{
    private const string _mockUserPassword = "Passw0rd!";
    private const int _terminalUserCount = 1400;
    private const int _terminalAdminCount = 200;
    private static readonly HttpClient _authHttpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
    });

    private static TerminalTestEnvironment? _environment;

    /// <summary>
    /// Starts the dependent hosts once for the test class.
    /// </summary>
    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        _environment = await TerminalTestEnvironment.StartAsync();
    }

    /// <summary>
    /// Shuts the dependent hosts down after the class completes.
    /// </summary>
    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        _authHttpClient.Dispose();

        if (_environment is not null)
        {
            await _environment.DisposeAsync();
        }
    }

    /// <summary>
    /// Smoke test for the browserless terminal cycle against the real HTTP, WebSocket, and TN3270 hosts.
    /// </summary>
    [TestMethod]
    public async Task TerminalProxySessionCycle_CompletesWithoutBrowser()
    {
        var environment = GetEnvironment();
        var tokenClient = new MockOidcClient(_authHttpClient, environment.MockIssuerUri);
        var mockUserName = ResolveTerminalCapableMockUserName(0);
        var authStopwatch = Stopwatch.StartNew();
        var accessToken = await tokenClient.AcquireAccessTokenAsync(
            mockUserName,
            _mockUserPassword,
            TestContext!.CancellationToken);
        authStopwatch.Stop();

        var sessionClient = new TerminalProxyLoadClient(environment.TerminalWebSocketUri, accessToken);
        var result = await sessionClient.RunAsync(TestContext.CancellationToken);

        Assert.AreEqual("IBM-3279-2-E", result.TerminalType);
        Assert.AreEqual("endpoint-server-terminated", result.EndReason);
        Assert.AreEqual("PITS (Platform Integrated Terminal Server)", result.TerminalEndpointDisplayName);
        TestContext!.WriteLine($"Auth latency (ms): {authStopwatch.Elapsed.TotalMilliseconds:F2}");
        TestContext.WriteLine($"Connect latency (ms): {result.Metrics.ConnectLatency.TotalMilliseconds:F2}");
        TestContext.WriteLine($"Ready latency (ms): {result.Metrics.ReadyLatency.TotalMilliseconds:F2}");
        TestContext.WriteLine($"Login latency (ms): {result.Metrics.LoginLatency.TotalMilliseconds:F2}");
        TestContext.WriteLine($"Logout latency (ms): {result.Metrics.LogoutLatency.TotalMilliseconds:F2}");
        TestContext.WriteLine($"Session duration (ms): {result.Metrics.SessionDuration.TotalMilliseconds:F2}");
    }

    /// <summary>
    /// Load test entry point. Set <c>TERMINAL_LOAD_ENABLED=1</c> to run it and optionally override
    /// <c>TERMINAL_LOAD_CONCURRENCY</c> if 1000 concurrent sessions are too heavy for the current machine.
    /// </summary>
    [TestMethod]
    [TestCategory("Load")]
    public async Task TerminalProxyLoad_HandlesConcurrentBrowserlessSessions()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("TERMINAL_LOAD_ENABLED"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Inconclusive(
                "Set TERMINAL_LOAD_ENABLED=1 to run the load test. The default target is 1000 concurrent sessions.");
        }

        var concurrency = ResolveConcurrency();
        var environment = GetEnvironment();
        var failures = new ConcurrentQueue<string>();
        var metrics = new ConcurrentBag<SessionMetricsSample>();
        var startGate = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessionTasks = Enumerable.Range(0, concurrency)
            .Select(index => RunSessionAsync(index, environment, startGate.Task, failures, metrics, TestContext!.CancellationToken))
            .ToArray();

        startGate.SetResult(null);
        await Task.WhenAll(sessionTasks);

        Assert.IsEmpty(
            failures,
            $"One or more concurrent sessions failed.{Environment.NewLine}{string.Join(Environment.NewLine, failures.Take(10))}{Environment.NewLine}{environment.GetDiagnostics()}");

        TestContext!.WriteLine($"Configured concurrent users: {concurrency}");
        TestContext.WriteLine($"Attempted sessions: {sessionTasks.Length}");
        TestContext.WriteLine($"Successfully completed sessions: {metrics.Count}");
        WriteMetricSummary("Auth", metrics.Select(static sample => sample.AuthLatency), TestContext);
        WriteMetricSummary("Connect", metrics.Select(static sample => sample.SessionMetrics.ConnectLatency), TestContext);
        WriteMetricSummary("Ready", metrics.Select(static sample => sample.SessionMetrics.ReadyLatency), TestContext);
        WriteMetricSummary("Login", metrics.Select(static sample => sample.SessionMetrics.LoginLatency), TestContext);
        WriteMetricSummary("Logout", metrics.Select(static sample => sample.SessionMetrics.LogoutLatency), TestContext);
        WriteMetricSummary("Session", metrics.Select(static sample => sample.SessionMetrics.SessionDuration), TestContext);
    }

    /// <summary>Gets or sets the MSTest context.</summary>
    public TestContext? TestContext { get; set; }

    private static int ResolveConcurrency()
    {
        var configured = Environment.GetEnvironmentVariable("TERMINAL_LOAD_CONCURRENCY");
        if (int.TryParse(configured, out var parsedConcurrency) && parsedConcurrency > 0)
        {
            return parsedConcurrency;
        }

        return 1000;
    }

    private static TerminalTestEnvironment GetEnvironment() =>
        _environment ?? throw new InvalidOperationException("The integration test environment has not been initialized.");

    private static string ResolveTerminalCapableMockUserName(int sessionIndex)
    {
        var normalizedIndex = Math.Abs(sessionIndex) % (_terminalUserCount + _terminalAdminCount);
        if (normalizedIndex < _terminalUserCount)
        {
            return $"terminaluser{normalizedIndex + 1:D4}@mockidp.local";
        }

        var adminIndex = normalizedIndex - _terminalUserCount;
        return $"terminaladmin{adminIndex + 1:D4}@mockidp.local";
    }

    private static async Task RunSessionAsync(
        int sessionIndex,
        TerminalTestEnvironment environment,
        Task startSignal,
        ConcurrentQueue<string> failures,
        ConcurrentBag<SessionMetricsSample> metrics,
        CancellationToken cancellationToken)
    {
        try
        {
            await startSignal.WaitAsync(cancellationToken);
            var tokenClient = new MockOidcClient(_authHttpClient, environment.MockIssuerUri);
            var mockUserName = ResolveTerminalCapableMockUserName(sessionIndex);
            var authStopwatch = Stopwatch.StartNew();
            var accessToken = await tokenClient.AcquireAccessTokenAsync(
                mockUserName,
                _mockUserPassword,
                cancellationToken);
            authStopwatch.Stop();
            var sessionClient = new TerminalProxyLoadClient(environment.TerminalWebSocketUri, accessToken);
            var result = await sessionClient.RunAsync(cancellationToken);

            if (!string.Equals(result.EndReason, "endpoint-server-terminated", StringComparison.Ordinal))
            {
                failures.Enqueue(
                    $"Session {sessionIndex} ended with unexpected reason '{result.EndReason}'.");
                return;
            }

            metrics.Add(new SessionMetricsSample(authStopwatch.Elapsed, result.Metrics));
        }
        catch (Exception exception)
        {
            failures.Enqueue($"Session {sessionIndex} failed: {exception}");
        }
    }

    private static void WriteMetricSummary(string label, IEnumerable<TimeSpan> samples, TestContext testContext)
    {
        var ordered = samples
            .Select(static sample => sample.TotalMilliseconds)
            .OrderBy(static sample => sample)
            .ToArray();

        if (ordered.Length == 0)
        {
            testContext.WriteLine($"{label} latency (ms): no samples");
            return;
        }

        testContext.WriteLine(
            $"{label} latency (ms): avg={ordered.Average():F2}, p50={Percentile(ordered, 0.50):F2}, p95={Percentile(ordered, 0.95):F2}, max={ordered[^1]:F2}");
    }

    private static double Percentile(IReadOnlyList<double> orderedValues, double percentile)
    {
        if (orderedValues.Count == 0)
        {
            return 0;
        }

        var position = percentile * (orderedValues.Count - 1);
        var lowerIndex = (int)Math.Floor(position);
        var upperIndex = (int)Math.Ceiling(position);

        if (lowerIndex == upperIndex)
        {
            return orderedValues[lowerIndex];
        }

        var fraction = position - lowerIndex;
        return orderedValues[lowerIndex] +
            ((orderedValues[upperIndex] - orderedValues[lowerIndex]) * fraction);
    }

    private sealed record SessionMetricsSample(
        TimeSpan AuthLatency,
        TerminalProxyLoadClient.SessionRunMetrics SessionMetrics);
}
