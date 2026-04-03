namespace Terminal.Common.Terminal;

/// <summary>
/// Represents one cell in the emulated 3270 presentation space.
/// </summary>
public sealed class TerminalCell
{
    /// <summary>Gets or sets the character displayed in the cell.</summary>
    public char Character { get; set; } = ' ';

    /// <summary>Gets or sets the cell foreground colour.</summary>
    public TerminalColor Foreground { get; set; } = TerminalColor.Default;

    /// <summary>Gets or sets the cell background colour.</summary>
    public TerminalColor Background { get; set; } = TerminalColor.Default;

    /// <summary>Gets or sets a value indicating whether the cell is protected from local editing.</summary>
    public bool IsProtected { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the cell contains a field attribute byte.</summary>
    public bool IsFieldAttribute { get; set; }

    /// <summary>Gets or sets a value indicating whether the cell should be rendered as non-display.</summary>
    public bool IsHidden { get; set; }

    /// <summary>Gets or sets a value indicating whether the cell belongs to an intensified field.</summary>
    public bool IsIntensified { get; set; }

    /// <summary>Gets or sets the zero-based field index owning this cell, or <c>-1</c> if none.</summary>
    public int FieldIndex { get; set; } = -1;
}
