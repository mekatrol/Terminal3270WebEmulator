using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Terminal.Console.Logging;

internal sealed class FileLoggerProvider(IOptions<FileLoggerOptions> options) : ILoggerProvider
{
    private readonly object _sync = new();
    private readonly string _filePath = ResolvePath(options.Value.Path);
    private StreamWriter? _writer;

    public ILogger CreateLogger(string categoryName)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        _writer ??= new StreamWriter(File.Open(_filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true,
        };

        return new FileLogger(categoryName, _writer, _sync);
    }

    public void Dispose() => _writer?.Dispose();

    private static string ResolvePath(string configuredPath) =>
        Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(configuredPath, AppContext.BaseDirectory);

    private sealed class FileLogger(string categoryName, StreamWriter writer, object sync) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            lock (sync)
            {
                writer.Write(DateTimeOffset.Now.ToString("O"));
                writer.Write(" [");
                writer.Write(logLevel);
                writer.Write("] ");
                writer.Write(categoryName);
                writer.Write(": ");
                writer.WriteLine(formatter(state, exception));

                if (exception is not null)
                {
                    writer.WriteLine(exception);
                }
            }
        }
    }
}
