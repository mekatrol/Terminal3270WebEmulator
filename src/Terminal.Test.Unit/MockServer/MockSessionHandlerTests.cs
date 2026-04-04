using System.Text;
using Terminal.MockServer;
using Terminal.MockServer.Screens;
using Terminal.MockServer.Services;

namespace Terminal.Test.Unit.MockServer;

/// <summary>
/// Verifies the mock-server sign-in gate that now sits in front of the main menu.
/// The regression risk here is behavioral rather than protocol-level: Enter on the login screen must only
/// advance when both submitted fields match the configured server values, otherwise the user must be looped
/// back to a failure screen so the SPA can exercise its retry path.
/// </summary>
[TestClass]
public sealed class MockSessionHandlerTests
{
    private static readonly byte[] _addressTable =
    [
        0x40, 0xC1, 0xC2, 0xC3, 0xC4, 0xC5, 0xC6, 0xC7,
        0xC8, 0xC9, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F,
        0x50, 0xD1, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7,
        0xD8, 0xD9, 0x5A, 0x5B, 0x5C, 0x5D, 0x5E, 0x5F,
        0x60, 0x61, 0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7,
        0xE8, 0xE9, 0x6A, 0x6B, 0x6C, 0x6D, 0x6E, 0x6F,
        0xF0, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7,
        0xF8, 0xF9, 0x7A, 0x7B, 0x7C, 0x7D, 0x7E, 0x7F,
    ];

    [TestMethod]
    public void CredentialsMatch_MissingPassword_ReturnsFalse()
    {
        var fieldValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["username"] = "DEMOUSER",
        };

        var options = new MockServerOptions
        {
            SignInUserId = "DEMOUSER",
            SignInPassword = "PASSWORD",
        };

        var result = MockSessionHandler.CredentialsMatch(fieldValues, options);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void ResolveNavigationTarget_LoginEnterWithInvalidCredentials_ReturnsLoginFailed()
    {
        var loginScreen = new ScreenDefinition
        {
            Id = "login",
            Description = "System Login",
            Rows = 24,
            Cols = 80,
            Navigation = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Enter"] = "main-menu",
            },
            Fields =
            [
                new FieldDefinition
                {
                    Row = 10,
                    Col = 26,
                    Type = "input",
                    Id = "username",
                    Length = 20,
                },
                new FieldDefinition
                {
                    Row = 12,
                    Col = 27,
                    Type = "input-hidden",
                    Id = "password",
                    Length = 20,
                },
            ],
        };

        var options = new MockServerOptions
        {
            SignInUserId = "DEMOUSER",
            SignInPassword = "PASSWORD",
        };

        var inboundRecord = BuildLoginInputRecord(loginScreen, "DEMOUSER", "WRONGPASS");

        var target = MockSessionHandler.ResolveNavigationTarget(loginScreen, "Enter", inboundRecord, options);

        Assert.AreEqual("login-failed", target);
    }

    [TestMethod]
    public void ResolveNavigationTarget_LoginEnterWithValidCredentials_ReturnsConfiguredSuccessTarget()
    {
        var loginScreen = new ScreenDefinition
        {
            Id = "login-failed",
            Description = "System Login Failed",
            Rows = 24,
            Cols = 80,
            Navigation = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Enter"] = "main-menu",
            },
            Fields =
            [
                new FieldDefinition
                {
                    Row = 10,
                    Col = 26,
                    Type = "input",
                    Id = "username",
                    Length = 20,
                },
                new FieldDefinition
                {
                    Row = 12,
                    Col = 27,
                    Type = "input-hidden",
                    Id = "password",
                    Length = 20,
                },
            ],
        };

        var options = new MockServerOptions
        {
            SignInUserId = "DEMOUSER",
            SignInPassword = "PASSWORD",
        };

        var inboundRecord = BuildLoginInputRecord(loginScreen, "DEMOUSER", "PASSWORD");

        var target = MockSessionHandler.ResolveNavigationTarget(loginScreen, "Enter", inboundRecord, options);

        Assert.AreEqual("main-menu", target);
    }

    private static byte[] BuildLoginInputRecord(ScreenDefinition screen, string userId, string password)
    {
        var usernameField = screen.Fields.Single(field => string.Equals(field.Id, "username", StringComparison.Ordinal));
        var passwordField = screen.Fields.Single(field => string.Equals(field.Id, "password", StringComparison.Ordinal));
        var usernameAddress = EncodeBufferAddress(usernameField.Row, usernameField.Col, screen.Cols);
        var passwordAddress = EncodeBufferAddress(passwordField.Row, passwordField.Col, screen.Cols);

        return
        [
            0x7D,
            0x40,
            0x40,
            0x11,
            usernameAddress.first,
            usernameAddress.second,
            .. EncodeEbcdic(userId),
            0x11,
            passwordAddress.first,
            passwordAddress.second,
            .. EncodeEbcdic(password),
        ];
    }

    private static (byte first, byte second) EncodeBufferAddress(int row, int col, int cols)
    {
        var address = ((row - 1) * cols) + (col - 1);
        return (_addressTable[address >> 6], _addressTable[address & 0x3F]);
    }

    private static byte[] EncodeEbcdic(string value)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(37).GetBytes(value);
    }
}
