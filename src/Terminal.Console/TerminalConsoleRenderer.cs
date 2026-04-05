using Terminal.Common.Terminal;

namespace Terminal.Console;

internal sealed class TerminalConsoleRenderer : IDisposable
{
    private bool _initialised;

    public void Initialise(Tn3270TerminalScreen screen)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (global::System.Console.BufferWidth < screen.Columns || global::System.Console.BufferHeight < screen.Rows)
                {
                    global::System.Console.SetBufferSize(
                        Math.Max(global::System.Console.BufferWidth, screen.Columns),
                        Math.Max(global::System.Console.BufferHeight, screen.Rows));
                }

                if (global::System.Console.WindowWidth < screen.Columns || global::System.Console.WindowHeight < screen.Rows)
                {
                    global::System.Console.SetWindowSize(
                        Math.Min(global::System.Console.LargestWindowWidth, screen.Columns),
                        Math.Min(global::System.Console.LargestWindowHeight, screen.Rows));
                }
            }
            catch (IOException)
            {
                // Some hosts reject console buffer or window resizing while still allowing rendering.
                // The emulator continues with the current console dimensions rather than failing startup.
            }
            catch (ArgumentOutOfRangeException)
            {
                // Console hosts can report transient size constraints that make a requested resize invalid.
                // Rendering can still proceed by using the host's current dimensions.
            }
            catch (PlatformNotSupportedException)
            {
                // Resize APIs are not supported on every console host, especially under redirected or
                // virtualized terminals, so startup falls back to rendering without resizing.
            }
        }

        global::System.Console.CursorVisible = false;
        global::System.Console.Clear();
        _initialised = true;
    }

    public void Render(Tn3270TerminalScreen screen)
    {
        if (!_initialised)
        {
            Initialise(screen);
        }

        for (var row = 0; row < screen.Rows; row++)
        {
            global::System.Console.SetCursorPosition(0, row);
            for (var column = 0; column < screen.Columns; column++)
            {
                var cell = screen.Cells[(row * screen.Columns) + column];
                global::System.Console.ForegroundColor = MapColor(cell.Foreground, intensified: cell.IsIntensified, isBackground: false);
                global::System.Console.BackgroundColor = MapColor(cell.Background, intensified: false, isBackground: true);
                global::System.Console.Write(cell.IsHidden ? ' ' : cell.Character);
            }
        }

        var (cursorRow, cursorColumn) = screen.GetCursorCoordinates();
        global::System.Console.ForegroundColor = ConsoleColor.Gray;
        global::System.Console.BackgroundColor = ConsoleColor.Black;
        global::System.Console.SetCursorPosition(cursorColumn, cursorRow);
        global::System.Console.CursorVisible = true;
    }

    public void Dispose()
    {
        global::System.Console.ResetColor();
        global::System.Console.CursorVisible = true;
    }

    private static ConsoleColor MapColor(TerminalColor color, bool intensified, bool isBackground) => color switch
    {
        TerminalColor.Blue or TerminalColor.DeepBlue => intensified ? ConsoleColor.Cyan : ConsoleColor.Blue,
        TerminalColor.Red => intensified ? ConsoleColor.Magenta : ConsoleColor.Red,
        TerminalColor.Pink or TerminalColor.Purple => ConsoleColor.Magenta,
        TerminalColor.Green or TerminalColor.PaleGreen => intensified ? ConsoleColor.Green : ConsoleColor.DarkGreen,
        TerminalColor.Turquoise or TerminalColor.PaleTurquoise => intensified ? ConsoleColor.Cyan : ConsoleColor.DarkCyan,
        TerminalColor.Yellow or TerminalColor.Orange => intensified ? ConsoleColor.Yellow : ConsoleColor.DarkYellow,
        TerminalColor.White => ConsoleColor.White,
        TerminalColor.Black => ConsoleColor.Black,
        TerminalColor.Grey => ConsoleColor.Gray,
        _ => isBackground ? ConsoleColor.Black : ConsoleColor.Gray,
    };
}
