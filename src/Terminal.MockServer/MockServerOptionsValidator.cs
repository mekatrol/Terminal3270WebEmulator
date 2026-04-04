using Microsoft.Extensions.Options;
using System.Net;

namespace Terminal.MockServer;

/// <summary>
/// Validates mock server configuration during host startup so invalid settings fail fast
/// before the listener begins accepting client connections.
/// </summary>
internal sealed class MockServerOptionsValidator : IValidateOptions<MockServerOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, MockServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.Host))
        {
            failures.Add("MockServer:Host is required.");
        }
        else if (!IPAddress.TryParse(options.Host, out _))
        {
            failures.Add("MockServer:Host must be a valid IP address.");
        }

        if (options.Port is < 1 or > IPEndPoint.MaxPort)
        {
            failures.Add(
                $"MockServer:Port must be between 1 and {IPEndPoint.MaxPort}.");
        }

        if (string.IsNullOrWhiteSpace(options.ScreensDirectory))
        {
            failures.Add("MockServer:ScreensDirectory is required.");
        }

        if (string.IsNullOrWhiteSpace(options.InitialScreen))
        {
            failures.Add("MockServer:InitialScreen is required.");
        }

        if (string.IsNullOrWhiteSpace(options.DeviceName))
        {
            failures.Add("MockServer:DeviceName is required.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
