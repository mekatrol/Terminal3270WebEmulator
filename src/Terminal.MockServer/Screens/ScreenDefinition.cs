namespace Terminal.MockServer.Screens;

/// <summary>
/// Top-level YAML screen definition. Each file in the screens directory deserialises to one of these.
/// </summary>
internal sealed class ScreenDefinition
{
    /// <summary>Unique identifier used in navigation targets and the initial-screen setting.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-readable name for this screen (informational only).</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Number of rows. Defaults to 24 (standard model-2 terminal).</summary>
    public int Rows { get; set; } = 24;

    /// <summary>Number of columns. Defaults to 80.</summary>
    public int Cols { get; set; } = 80;

    /// <summary>Initial cursor position (1-based row/col).</summary>
    public CursorPosition Cursor { get; set; } = new();

    /// <summary>All fields that appear on the screen, in any order.</summary>
    public List<FieldDefinition> Fields { get; set; } = [];

    /// <summary>
    /// Maps AID key names to target screen IDs.
    /// Keys: "Enter", "Clear", "PA1", "PA2", "PA3", "PF1"–"PF24".
    /// Values: a screen id, or the special value "exit" to close the session.
    /// </summary>
    public Dictionary<string, string> Navigation { get; set; } = [];
}

/// <summary>Initial cursor position within a screen (1-based).</summary>
internal sealed class CursorPosition
{
    /// <summary>Row number, 1-based.</summary>
    public int Row { get; set; } = 1;

    /// <summary>Column number, 1-based.</summary>
    public int Col { get; set; } = 1;
}

/// <summary>
/// A single field on a screen.
/// The <see cref="Row"/> and <see cref="Col"/> properties identify where the field <em>content</em> begins
/// (1-based). The 3270 attribute byte is automatically placed one cell before that position.
/// </summary>
internal sealed class FieldDefinition
{
    /// <summary>Row where field content starts, 1-based.</summary>
    public int Row { get; set; } = 1;

    /// <summary>Column where field content starts, 1-based.</summary>
    public int Col { get; set; } = 1;

    /// <summary>
    /// Field type. One of:
    /// <list type="bullet">
    ///   <item><c>label</c> — protected text, user cannot modify.</item>
    ///   <item><c>input</c> — unprotected, alphanumeric input field.</item>
    ///   <item><c>input-hidden</c> — unprotected, non-display (password) field.</item>
    ///   <item><c>input-numeric</c> — unprotected, numeric-only field.</item>
    /// </list>
    /// </summary>
    public string Type { get; set; } = "label";

    /// <summary>Text content for label fields. Ignored for input fields.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Length in characters for input fields. Ignored for label fields (length is derived from
    /// <see cref="Text"/>).
    /// </summary>
    public int Length { get; set; } = 1;

    /// <summary>Logical identifier for this field (informational; used for future extensions).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>When true, the field is rendered at intensified brightness.</summary>
    public bool Intensified { get; set; }

    /// <summary>
    /// Optional 3279 foreground colour name for the field.
    /// Supported values are <c>default</c>, <c>blue</c>, <c>red</c>, <c>pink</c>, <c>green</c>,
    /// <c>turquoise</c>, <c>yellow</c>, <c>white</c>, <c>black</c>, <c>deep-blue</c>,
    /// <c>orange</c>, <c>purple</c>, <c>pale-green</c>, <c>pale-turquoise</c>, and <c>grey</c>.
    /// When omitted, the mock server leaves the field at the terminal default colour.
    /// </summary>
    public string? Foreground { get; set; }

    /// <summary>
    /// Optional 3279 background colour name for the field.
    /// Supported values match <see cref="Foreground"/>.
    /// When omitted, the mock server leaves the field at the terminal default colour.
    /// </summary>
    public string? Background { get; set; }
}
