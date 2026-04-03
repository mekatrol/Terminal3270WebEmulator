using Terminal.Common.Protocol;

namespace Terminal.Test.Unit.Protocol;

[TestClass]
public sealed class Tn3270DataStreamParserTests
{
    [TestMethod]
    public void Describe_WriteRecord_IncludesWccAndFieldMessages()
    {
        byte[] data =
        [
            0xF1, // Write
            0xC3, // WCC
            0x1D, 0x20, // Start Field
            0x11, 0x40, 0x41, // Set Buffer Address
            0x13, // Insert Cursor
        ];

        var description = Tn3270DataStreamParser.Describe(data);

        Assert.AreEqual("Write", description.CommandName);
        Assert.AreEqual((byte)0xC3, description.Wcc);
        CollectionAssert.AreEqual(
            new[]
            {
                "WCC=0xC3",
                "Field order: Start Field Attribute=0x20",
                "Order: Set Buffer Address Operand=0x4041",
                "Order: Insert Cursor",
            },
            description.Messages.ToArray());
    }

    [TestMethod]
    public void Describe_WriteStructuredField_ListsStructuredFieldMetadata()
    {
        byte[] data =
        [
            0xF3, // Write Structured Field
            0x00, 0x05, // length
            0x01, // structured field id
            0xAA, 0xBB, // payload
        ];

        var description = Tn3270DataStreamParser.Describe(data);

        Assert.AreEqual("Write Structured Field", description.CommandName);
        Assert.IsNull(description.Wcc);
        CollectionAssert.AreEqual(
            new[]
            {
                "Structured field 0: Id=0x01 Length=5 DataLength=2",
            },
            description.Messages.ToArray());
    }

    [TestMethod]
    public void Describe_TruncatedStartFieldExtended_ReportsProblem()
    {
        byte[] data =
        [
            0xF5, // Erase/Write
            0x00, // WCC
            0x29, 0x02, 0xC0, 0x00, // missing second type/value pair
        ];

        var description = Tn3270DataStreamParser.Describe(data);

        Assert.AreEqual("Erase/Write", description.CommandName);
        CollectionAssert.AreEqual(
            new[]
            {
                "WCC=0x00",
                "Start Field Extended order truncated: PairCount=2 Available=2",
            },
            description.Messages.ToArray());
    }
}
