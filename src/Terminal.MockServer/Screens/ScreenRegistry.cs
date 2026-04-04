using System.Globalization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Terminal.MockServer.Screens;

/// <summary>
/// Loads and indexes <see cref="ScreenDefinition"/> objects from a directory of YAML files.
/// </summary>
internal sealed partial class ScreenRegistry
{
    private static readonly HashSet<string> _supportedColours =
    [
        "default",
        "blue",
        "red",
        "pink",
        "green",
        "turquoise",
        "yellow",
        "white",
        "black",
        "deep-blue",
        "orange",
        "purple",
        "pale-green",
        "pale-turquoise",
        "grey",
    ];

    private static readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly Dictionary<string, ScreenDefinition> _screens;

    private ScreenRegistry(Dictionary<string, ScreenDefinition> screens)
    {
        _screens = screens;
    }

    /// <summary>
    /// Loads screens from <paramref name="directory"/>, validates them, and returns an immutable registry.
    /// Invalid content causes startup to fail so bad mock definitions are not discovered only after a client
    /// connects.
    /// </summary>
    public static ScreenRegistry LoadFromDirectory(string directory, ILogger<ScreenRegistry> logger)
    {
        if (!Directory.Exists(directory))
        {
            LogDirectoryNotFound(logger, directory);
            throw new DirectoryNotFoundException($"Screens directory was not found: {directory}");
        }

        Dictionary<string, ScreenDefinition> screens = new(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(directory, "*.yaml", SearchOption.TopDirectoryOnly))
        {
            var yaml = File.ReadAllText(file);
            var screen = _deserializer.Deserialize<ScreenDefinition>(yaml)
                ?? throw new InvalidOperationException($"Screen file '{file}' did not contain a screen definition.");

            ValidateScreen(screen, file);

            if (!screens.TryAdd(screen.Id, NormalizeScreen(screen)))
            {
                throw new InvalidOperationException(
                    $"Duplicate screen id '{screen.Id}' was found in '{file}'.");
            }

            LogLoaded(logger, screen.Id, screen.Description, file);
        }

        if (screens.Count == 0)
        {
            throw new InvalidOperationException(
                $"No screen definition files were found in '{directory}'.");
        }

        LogLoadComplete(logger, screens.Count);
        return new ScreenRegistry(screens);
    }

    /// <summary>
    /// Looks up a screen by its <paramref name="id"/>.
    /// Returns <see langword="false"/> if the id is not registered.
    /// </summary>
    public bool TryGet(string id, out ScreenDefinition? screen) =>
        _screens.TryGetValue(id, out screen);

    private static ScreenDefinition NormalizeScreen(ScreenDefinition screen)
    {
        screen.Navigation = new Dictionary<string, string>(screen.Navigation, StringComparer.OrdinalIgnoreCase);
        return screen;
    }

    private static void ValidateScreen(ScreenDefinition screen, string file)
    {
        if (string.IsNullOrWhiteSpace(screen.Id))
        {
            throw new InvalidOperationException($"Screen file '{file}' is missing the required 'id' field.");
        }

        if (screen.Rows <= 0 || screen.Cols <= 0)
        {
            throw new InvalidOperationException(
                $"Screen '{screen.Id}' in '{file}' must define positive row and column counts.");
        }

        ValidatePosition(screen.Id, file, "cursor", screen.Cursor.Row, screen.Cursor.Col, screen.Rows, screen.Cols);

        foreach (var field in screen.Fields)
        {
            ValidateField(screen, field, file);
        }
    }

    private static void ValidateField(ScreenDefinition screen, FieldDefinition field, string file)
    {
        ValidatePosition(screen.Id, file, $"field '{field.Id}'", field.Row, field.Col, screen.Rows, screen.Cols);

        var type = field.Type.Trim().ToLowerInvariant();
        var isKnownType = type is "label" or "input" or "input-hidden" or "input-numeric";
        if (!isKnownType)
        {
            throw new InvalidOperationException(
                $"Screen '{screen.Id}' in '{file}' has unsupported field type '{field.Type}'.");
        }

        if (type == "label" && string.IsNullOrEmpty(field.Text))
        {
            throw new InvalidOperationException(
                $"Screen '{screen.Id}' in '{file}' has a label field at row {field.Row}, col {field.Col} with no text.");
        }

        if (type != "label" && field.Length <= 0)
        {
            throw new InvalidOperationException(
                $"Screen '{screen.Id}' in '{file}' has input field '{field.Id}' with non-positive length.");
        }

        ValidateColour(screen.Id, file, field, "foreground", field.Foreground);
        ValidateColour(screen.Id, file, field, "background", field.Background);
    }

    private static void ValidatePosition(
        string screenId,
        string file,
        string name,
        int row,
        int col,
        int rows,
        int cols)
    {
        if (row < 1 || row > rows || col < 1 || col > cols)
        {
            throw new InvalidOperationException(
                $"Screen '{screenId}' in '{file}' has {name} outside the configured screen bounds.");
        }
    }

    private static void ValidateColour(
        string screenId,
        string file,
        FieldDefinition field,
        string propertyName,
        string? colour)
    {
        if (string.IsNullOrWhiteSpace(colour))
        {
            return;
        }

        var normalized = colour.Trim().ToLower(CultureInfo.InvariantCulture);
        if (_supportedColours.Contains(normalized))
        {
            return;
        }

        throw new InvalidOperationException(
            $"Screen '{screenId}' in '{file}' has field '{field.Id}' at row {field.Row}, col {field.Col} with unsupported {propertyName} colour '{colour}'.");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Screens directory not found: {Directory}")]
    private static partial void LogDirectoryNotFound(ILogger logger, string directory);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded screen '{Id}' ({Description}) from {File}")]
    private static partial void LogLoaded(ILogger logger, string id, string description, string file);

    [LoggerMessage(Level = LogLevel.Information, Message = "Screen registry ready: {Count} screen(s) loaded")]
    private static partial void LogLoadComplete(ILogger logger, int count);
}
