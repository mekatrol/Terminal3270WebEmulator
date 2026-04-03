namespace Terminal.Common.Terminal;

/// <summary>
/// Represents one 3270 field defined by a field attribute byte.
/// </summary>
public sealed class TerminalField
{
    /// <summary>Gets or sets the address containing the field attribute byte.</summary>
    public int AttributeAddress { get; set; }

    /// <summary>Gets or sets the first data address in the field.</summary>
    public int StartAddress { get; set; }

    /// <summary>Gets or sets the last data address in the field.</summary>
    public int EndAddress { get; set; }

    /// <summary>Gets or sets a value indicating whether the field is protected.</summary>
    public bool IsProtected { get; set; }

    /// <summary>Gets or sets a value indicating whether the field allows only numeric data.</summary>
    public bool IsNumeric { get; set; }

    /// <summary>Gets or sets a value indicating whether the field is non-display.</summary>
    public bool IsHidden { get; set; }

    /// <summary>Gets or sets a value indicating whether the field should be drawn with intensified styling.</summary>
    public bool IsIntensified { get; set; }

    /// <summary>Gets or sets a value indicating whether the field has been modified locally.</summary>
    public bool IsModified { get; set; }

    /// <summary>Gets or sets the field foreground colour.</summary>
    public TerminalColor Foreground { get; set; } = TerminalColor.Default;

    /// <summary>Gets or sets the field background colour.</summary>
    public TerminalColor Background { get; set; } = TerminalColor.Default;
}
