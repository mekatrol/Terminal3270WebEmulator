namespace Terminal.Common.Options;

public sealed class Tn3270EOptions
{
    public const string SectionName = "Tn3270E";

    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 23;

    public string TerminalType { get; set; } = "IBM-3278-2-E";

    public string? DeviceName { get; set; }

    public int ConnectionTimeoutSeconds { get; set; } = 30;
}
