using Terminal.MockServer.Screens;

namespace Terminal.Test.Unit.MockServer;

/// <summary>
/// Verifies that the mock server emits 3270 extended-colour orders so the SPA can be tested
/// against realistic host output instead of a monochrome-only stream.
/// IBM documents colour support via Start Field Extended (SFE) attributes in the 3270 data stream
/// architecture. See IBM 3270 Data Stream Programmer's Reference:
/// https://publibfp.boulder.ibm.com/epubs/pdf/ga239005.pdf
/// </summary>
[TestClass]
public sealed class DataStreamEncoderTests
{
    [TestMethod]
    public void Encode_FieldSpecifiesColours_EmitsStartFieldExtendedWithColourPairs()
    {
        var screen = new ScreenDefinition
        {
            Id = "colour-demo",
            Description = "Colour demo",
            Rows = 24,
            Cols = 80,
            Fields =
            [
                new FieldDefinition
                {
                    Row = 1,
                    Col = 2,
                    Type = "label",
                    Text = "A",
                    Foreground = "red",
                    Background = "blue",
                },
            ],
        };

        var encoded = DataStreamEncoder.Encode(screen);

        CollectionAssert.AreEqual(
            new byte[] { 0xF5, 0xC3, 0x11, 0x40, 0x40, 0x29, 0x03, 0xC0, 0x20, 0x42, 0xF2, 0x45, 0xF1, 0xC1 },
            encoded.Take(14).ToArray());
    }
}
