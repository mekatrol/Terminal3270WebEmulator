namespace Terminal.MockServer;

/// <summary>
/// Configuration for the TN3270E mock server.
/// </summary>
internal sealed class MockServerOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "MockServer";

    /// <summary>IP address to listen on. Use "0.0.0.0" to accept all interfaces.</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>TCP port to listen on.</summary>
    public int Port { get; set; } = 3270;

    /// <summary>Directory containing YAML screen definition files.</summary>
    public string ScreensDirectory { get; set; } = "screens";

    /// <summary>ID of the screen sent to a client immediately after negotiation.</summary>
    public string InitialScreen { get; set; } = "login";

    /// <summary>Device name advertised to the client during TN3270E negotiation.</summary>
    public string DeviceName { get; set; } = "MOCK0001";

    /// <summary>
    /// User ID that the mock server accepts on the sign-in screen.
    /// The mock server exists to exercise the SPA login path before a real host integration is wired up,
    /// so a fixed configured value keeps the test path deterministic and easy to reset between sessions.
    /// </summary>
    public string SignInUserId { get; set; } = "DEMOUSER";

    /// <summary>
    /// Password that the mock server accepts on the sign-in screen.
    /// This is intentionally configuration-driven rather than hard-coded in the session handler so local
    /// test environments can choose their own known value without recompiling the mock server.
    /// </summary>
    public string SignInPassword { get; set; } = "PASSWORD";

    /// <summary>
    /// When true, the server skips TN3270E negotiation and establishes a classic TN3270 session.
    /// This is useful when validating basic interoperability with terminal emulators before
    /// introducing TN3270E framing.
    /// </summary>
    public bool PreferPlainTn3270 { get; set; }
}
