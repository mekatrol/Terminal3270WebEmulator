using Terminal.Common.Terminal;

namespace Terminal.Test.Unit.Terminal;

[TestClass]
public sealed class Tn3270TerminalScreenTests
{
    [TestMethod]
    public void Constructor_Model2Terminal_UsesExpectedDimensions()
    {
        var screen = new Tn3270TerminalScreen("IBM-3278-2-E");

        Assert.AreEqual(80, screen.Columns);
        Assert.AreEqual(24, screen.Rows);
    }

    [TestMethod]
    public void ApplyInboundRecord_WriteWithUnprotectedField_RendersTextAndEditableField()
    {
        var screen = new Tn3270TerminalScreen(80, 24);

        byte[] record =
        [
            0xF5, 0x00,
            0x1D, 0x20,
            0xC8, 0xC5, 0xD3, 0xD3, 0xD6,
            0x11, 0x40, 0x4A,
            0x1D, 0x00,
            0x13,
        ];

        screen.ApplyInboundRecord(record);

        Assert.AreEqual('H', screen.Cells[1].Character);
        Assert.AreEqual('O', screen.Cells[5].Character);
        Assert.IsTrue(screen.Cells[1].IsProtected);
        Assert.IsFalse(screen.Cells[11].IsProtected);
        Assert.AreEqual(11, screen.CursorAddress);
        Assert.HasCount(2, screen.Fields);
        Assert.IsFalse(screen.Fields[1].IsProtected);
    }

    [TestMethod]
    public void ApplyInboundRecord_StartFieldExtended_UsesConfiguredColours()
    {
        var screen = new Tn3270TerminalScreen(80, 24);

        byte[] record =
        [
            0xF5, 0x00,
            0x29, 0x03,
            0xC0, 0x00,
            0x42, 0xF2,
            0x45, 0xF1,
            0xC1,
        ];

        screen.ApplyInboundRecord(record);

        Assert.AreEqual(TerminalColor.Red, screen.Cells[1].Foreground);
        Assert.AreEqual(TerminalColor.Blue, screen.Cells[1].Background);
        Assert.IsFalse(screen.Cells[1].IsProtected);
        Assert.AreEqual('A', screen.Cells[1].Character);
    }

    [TestMethod]
    public void BuildReadModifiedRecord_ModifiedField_EncodesAidCursorAndFieldData()
    {
        var screen = new Tn3270TerminalScreen(80, 24);

        byte[] record =
        [
            0xF5, 0x00,
            0x11, 0x40, 0x4A,
            0x1D, 0x00,
            0x13,
        ];

        screen.ApplyInboundRecord(record);

        Assert.IsTrue(screen.TryWriteCharacter('U'));
        Assert.IsTrue(screen.TryWriteCharacter('S'));

        var result = screen.BuildReadModifiedRecord();

        CollectionAssert.AreEqual(
            new byte[] { 0x7D, 0x40, 0x4D, 0x11, 0x40, 0x4B, 0xE4, 0xE2 },
            result.Take(8).ToArray());
    }
}
