namespace Terminal.MockServer.Logging;

internal sealed class FileLoggerOptions
{
    public const string SectionName = "FileLogging";

    public string Path { get; set; } = "logs/terminal-mockserver.log";
}
