namespace Terminal.MockServer;

/// <summary>
/// Resolves file-system paths for the mock server relative to the deployed application
/// directory so the executable behaves consistently regardless of the caller's working directory.
/// </summary>
internal static class MockServerPaths
{
    /// <summary>
    /// Resolves the configured screens directory to an absolute path.
    /// </summary>
    public static string ResolveScreensDirectory(string screensDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(screensDirectory);

        return Path.IsPathRooted(screensDirectory)
            ? Path.GetFullPath(screensDirectory)
            : Path.GetFullPath(screensDirectory, AppContext.BaseDirectory);
    }
}
