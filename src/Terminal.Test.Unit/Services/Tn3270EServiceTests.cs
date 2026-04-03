using Microsoft.Extensions.Logging.Abstractions;
using System.IO.Pipelines;
using Terminal.Common.Models;
using Terminal.Common.Protocol;
using Terminal.Common.Services;
using Terminal.Common.Services.Implementation;

namespace Terminal.Test.Unit.Services;

/// <summary>
/// Drives <see cref="Tn3270EService"/> through a bidirectional in-memory pipe so
/// that all protocol logic is exercised without a real network.
/// </summary>
[TestClass]
public sealed class Tn3270EServiceTests
{
    // -------------------------------------------------------------------------
    // ConnectAsync
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task ConnectAsync_SetsIsConnectedAndHostPort()
    {
        using var env = new TestEnvironment();
        await env.Service.ConnectAsync("testhost", 23, CancellationToken.None);

        Assert.IsTrue(env.Service.IsConnected);
        Assert.AreEqual("testhost", env.Service.ConnectedHost);
        Assert.AreEqual(23, env.Service.ConnectedPort);
    }

    [TestMethod]
    public async Task ConnectAsync_WhenAlreadyConnected_ThrowsInvalidOperation()
    {
        using var env = new TestEnvironment();
        await env.Service.ConnectAsync("testhost", 23, CancellationToken.None);

        var threw = false;
        try
        {
            await env.Service.ConnectAsync("testhost", 23, CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        Assert.IsTrue(threw, "Expected InvalidOperationException when connecting twice");
    }

    // -------------------------------------------------------------------------
    // DisconnectAsync
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task DisconnectAsync_ClearsConnectedState()
    {
        using var env = new TestEnvironment();
        await env.Service.ConnectAsync("testhost", 23, CancellationToken.None);
        await env.Service.DisconnectAsync(CancellationToken.None);

        Assert.IsFalse(env.Service.IsConnected);
        Assert.IsNull(env.Service.ConnectedHost);
        Assert.AreEqual(0, env.Service.ConnectedPort);
    }

    [TestMethod]
    public async Task DisconnectAsync_WhenNotConnected_DoesNotThrow()
    {
        using var env = new TestEnvironment();

        // Should complete without exception.
        await env.Service.DisconnectAsync(CancellationToken.None);
    }

    // -------------------------------------------------------------------------
    // NegotiateAsync
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task NegotiateAsync_SuccessfulHandshake_ReturnsTrue()
    {
        using var env = new TestEnvironment();
        await env.Service.ConnectAsync("testhost", 23, CancellationToken.None);

        // Run negotiation concurrently — one side is the service, the other is our fake server.
        var negotiateTask = env.Service.NegotiateAsync(
            "IBM-3278-2-E", null, CancellationToken.None);

        var serverTask = RunFakeServerNegotiationAsync(env, "IBM-3278-2-E", "LU01");

        await Task.WhenAll(negotiateTask, serverTask);

        Assert.IsTrue(await negotiateTask);
    }

    [TestMethod]
    public async Task NegotiateAsync_ServerRejectsDeviceType_ReturnsFalse()
    {
        using var env = new TestEnvironment();
        await env.Service.ConnectAsync("testhost", 23, CancellationToken.None);

        var negotiateTask = env.Service.NegotiateAsync(
            "IBM-3278-2-E", null, CancellationToken.None);

        var serverTask = RunFakeServerRejectionAsync(env, TelnetConstants.ReasonDeviceInUse);

        await Task.WhenAll(negotiateTask, serverTask);

        Assert.IsFalse(await negotiateTask);
    }

    [TestMethod]
    public async Task NegotiateAsync_PlainTn3270Handshake_ReturnsTrue()
    {
        using var env = new TestEnvironment();
        await env.Service.ConnectAsync("testhost", 23, CancellationToken.None);

        var negotiateTask = env.Service.NegotiateAsync(
            "IBM-3278-2-E", null, CancellationToken.None);

        var serverTask = RunFakePlainServerNegotiationAsync(env, [0xF5, 0x00, 0x01]);

        await Task.WhenAll(negotiateTask, serverTask);

        Assert.IsTrue(await negotiateTask);
    }

    // -------------------------------------------------------------------------
    // SendAsync / ReceiveAsync
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task SendAsync_FrameArrivesOnServerSideWithCorrectStructure()
    {
        using var env = new TestEnvironment();
        await env.Service.ConnectAsync("testhost", 23, CancellationToken.None);

        var payload = new byte[] { 0x11, 0x22, 0x33 };
        var frame = new Tn3270EFrame(
            Tn3270EDataType.Data3270, 0x00, 0x00, 1, payload);

        var sendTask = env.Service.SendAsync(frame, CancellationToken.None);

        // Read from the server side of the pipe.
        var received = await ReadFrameFromPipeAsync(env);
        await sendTask;

        Assert.AreEqual(Tn3270EDataType.Data3270, received.DataType);
        Assert.AreEqual((ushort)1, received.SequenceNumber);
        CollectionAssert.AreEqual(payload, received.Data.ToArray());
    }

    [TestMethod]
    public async Task ReceiveAsync_FrameSentByServer_ParsedCorrectly()
    {
        using var env = new TestEnvironment();
        await env.Service.ConnectAsync("testhost", 23, CancellationToken.None);

        var payload = new byte[] { 0xAA, 0xBB };
        var writeTask = WriteFrameToPipeAsync(
            env, Tn3270EDataType.SscpLuData, 0x00, 0x00, 42, payload);

        var receiveTask = env.Service.ReceiveAsync(CancellationToken.None);

        await Task.WhenAll(writeTask, receiveTask);

        var frame = await receiveTask;
        Assert.AreEqual(Tn3270EDataType.SscpLuData, frame.DataType);
        Assert.AreEqual((ushort)42, frame.SequenceNumber);
        CollectionAssert.AreEqual(payload, frame.Data.ToArray());
    }

    [TestMethod]
    public async Task ReceiveAsync_FrameWithIacInData_UnescapedCorrectly()
    {
        using var env = new TestEnvironment();
        await env.Service.ConnectAsync("testhost", 23, CancellationToken.None);

        // Data contains 0xFF which gets escaped to 0xFF 0xFF on the wire.
        var originalPayload = new byte[] { 0x01, 0xFF, 0x02 };
        var writeTask = WriteFrameToPipeAsync(
            env, Tn3270EDataType.Data3270, 0x00, 0x00, 1, originalPayload);

        var receiveTask = env.Service.ReceiveAsync(CancellationToken.None);
        await Task.WhenAll(writeTask, receiveTask);

        var frame = await receiveTask;
        CollectionAssert.AreEqual(originalPayload, frame.Data.ToArray());
    }

    [TestMethod]
    public async Task ReceiveAsync_PlainTn3270Record_ReturnsSynthetic3270Frame()
    {
        using var env = new TestEnvironment();
        await env.Service.ConnectAsync("testhost", 23, CancellationToken.None);

        var negotiateTask = env.Service.NegotiateAsync(
            "IBM-3278-2-E", null, CancellationToken.None);

        var expectedPayload = new byte[] { 0xF5, 0x42, 0x11, 0x40 };
        var serverTask = RunFakePlainServerNegotiationAsync(env, expectedPayload);

        await Task.WhenAll(negotiateTask, serverTask);

        var frame = await env.Service.ReceiveAsync(CancellationToken.None);

        Assert.AreEqual(Tn3270EDataType.Data3270, frame.DataType);
        Assert.AreEqual((ushort)0, frame.SequenceNumber);
        CollectionAssert.AreEqual(expectedPayload, frame.Data.ToArray());
    }

    [TestMethod]
    public async Task SendAsync_WhenNegotiatedPlainTn3270_WritesRawRecordWithoutHeader()
    {
        using var env = new TestEnvironment();
        await env.Service.ConnectAsync("testhost", 23, CancellationToken.None);

        var negotiateTask = env.Service.NegotiateAsync(
            "IBM-3278-2-E", null, CancellationToken.None);

        var serverTask = RunFakePlainServerNegotiationAsync(env, [0xF5]);

        await Task.WhenAll(negotiateTask, serverTask);

        var payload = new byte[] { 0x11, 0x22, 0x33 };
        var frame = new Tn3270EFrame(
            Tn3270EDataType.Data3270, 0x99, 0x88, 123, payload);

        await env.Service.SendAsync(frame, CancellationToken.None);

        var received = await ReadPlainRecordFromPipeAsync(env);
        CollectionAssert.AreEqual(payload, received);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Simulates the server side of a successful TN3270E negotiation.
    /// </summary>
    private static async Task RunFakeServerNegotiationAsync(
        TestEnvironment env,
        string terminalType,
        string deviceName)
    {
        // 1. Send IAC DO TN3270E
        await env.ServerWriteAsync(
            TelnetProtocol.BuildOptionCommand(TelnetConstants.Do, TelnetConstants.OptionTn3270E));

        // 2. Read IAC WILL TN3270E from client
        await env.ServerReadBytesAsync(3); // 3-byte command

        // 3. Send IAC SB TN3270E DEVICE-TYPE SEND IAC SE
        byte[] sendDeviceType = [TelnetConstants.Tn3270EDeviceType, TelnetConstants.Tn3270ESend];
        await env.ServerWriteAsync(
            TelnetProtocol.BuildSubnegotiation(TelnetConstants.OptionTn3270E, sendDeviceType));

        // 4. Read client DEVICE-TYPE REQUEST sub-negotiation (variable length — drain until IAC SE)
        await env.ServerDrainSubnegotiationAsync();

        // 5. Send DEVICE-TYPE IS <type> CONNECT <name>
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(terminalType);
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(deviceName);
        var isPayload = new byte[2 + typeBytes.Length + 1 + nameBytes.Length];
        isPayload[0] = TelnetConstants.Tn3270EDeviceType;
        isPayload[1] = TelnetConstants.Tn3270EIs;
        typeBytes.CopyTo(isPayload, 2);
        isPayload[2 + typeBytes.Length] = TelnetConstants.Tn3270EConnect;
        nameBytes.CopyTo(isPayload, 2 + typeBytes.Length + 1);

        await env.ServerWriteAsync(
            TelnetProtocol.BuildSubnegotiation(TelnetConstants.OptionTn3270E, isPayload));
    }

    /// <summary>
    /// Simulates the server side of a rejected TN3270E negotiation.
    /// </summary>
    private static async Task RunFakeServerRejectionAsync(TestEnvironment env, byte reasonCode)
    {
        await env.ServerWriteAsync(
            TelnetProtocol.BuildOptionCommand(TelnetConstants.Do, TelnetConstants.OptionTn3270E));

        await env.ServerReadBytesAsync(3);

        byte[] sendDeviceType = [TelnetConstants.Tn3270EDeviceType, TelnetConstants.Tn3270ESend];
        await env.ServerWriteAsync(
            TelnetProtocol.BuildSubnegotiation(TelnetConstants.OptionTn3270E, sendDeviceType));

        await env.ServerDrainSubnegotiationAsync();

        byte[] rejectPayload =
        [
            TelnetConstants.Tn3270EDeviceType,
            TelnetConstants.Tn3270EReject,
            TelnetConstants.Tn3270EReason,
            reasonCode,
        ];
        await env.ServerWriteAsync(
            TelnetProtocol.BuildSubnegotiation(TelnetConstants.OptionTn3270E, rejectPayload));
    }

    private static async Task RunFakePlainServerNegotiationAsync(
        TestEnvironment env,
        byte[] firstRecordPayload)
    {
        await env.ServerWriteAsync(
            TelnetProtocol.BuildOptionCommand(TelnetConstants.Do, TelnetConstants.OptionTerminalType));

        var willTerminalType = await env.ServerReadBytesAsync(3);
        CollectionAssert.AreEqual(
            TelnetProtocol.BuildOptionCommand(TelnetConstants.Will, TelnetConstants.OptionTerminalType),
            willTerminalType);

        await env.ServerWriteAsync(
            TelnetProtocol.BuildSubnegotiation(
                TelnetConstants.OptionTerminalType,
                [TelnetConstants.TerminalTypeSend]));

        var terminalTypeResponse = await env.ServerReadSubnegotiationAsync();
        Assert.AreEqual(TelnetConstants.OptionTerminalType, terminalTypeResponse.Option);
        Assert.AreEqual(TelnetConstants.TerminalTypeIs, terminalTypeResponse.Data[0]);

        await env.ServerWriteAsync(
            TelnetProtocol.BuildOptionCommand(TelnetConstants.Do, TelnetConstants.OptionEor));
        await env.ServerWriteAsync(
            TelnetProtocol.BuildOptionCommand(TelnetConstants.Will, TelnetConstants.OptionEor));

        var eorWill = await env.ServerReadBytesAsync(3);
        var eorDo = await env.ServerReadBytesAsync(3);
        CollectionAssert.AreEqual(
            TelnetProtocol.BuildOptionCommand(TelnetConstants.Will, TelnetConstants.OptionEor),
            eorWill);
        CollectionAssert.AreEqual(
            TelnetProtocol.BuildOptionCommand(TelnetConstants.Do, TelnetConstants.OptionEor),
            eorDo);

        await env.ServerWriteAsync(
            TelnetProtocol.BuildOptionCommand(TelnetConstants.Do, TelnetConstants.OptionBinary));
        await env.ServerWriteAsync(
            TelnetProtocol.BuildOptionCommand(TelnetConstants.Will, TelnetConstants.OptionBinary));

        var binaryWill = await env.ServerReadBytesAsync(3);
        var binaryDo = await env.ServerReadBytesAsync(3);
        CollectionAssert.AreEqual(
            TelnetProtocol.BuildOptionCommand(TelnetConstants.Will, TelnetConstants.OptionBinary),
            binaryWill);
        CollectionAssert.AreEqual(
            TelnetProtocol.BuildOptionCommand(TelnetConstants.Do, TelnetConstants.OptionBinary),
            binaryDo);

        var record = new byte[firstRecordPayload.Length + 2];
        firstRecordPayload.CopyTo(record, 0);
        record[^2] = TelnetConstants.Iac;
        record[^1] = TelnetConstants.Eor;
        await env.ServerWriteAsync(record);
    }

    /// <summary>
    /// Reads one TN3270E frame from the client-to-server pipe (i.e. what the service sent).
    /// </summary>
    private static async Task<Tn3270EFrame> ReadFrameFromPipeAsync(TestEnvironment env)
    {
        var payload = new List<byte>();
        var expectEor = false;

        while (true)
        {
            var b = await env.ServerReadByteAsync();

            if (expectEor)
            {
                if (b == TelnetConstants.Eor)
                {
                    break;
                }

                if (b == TelnetConstants.Iac)
                {
                    payload.Add(TelnetConstants.Iac);
                }

                expectEor = false;
                continue;
            }

            if (b == TelnetConstants.Iac)
            {
                expectEor = true;
                continue;
            }

            payload.Add(b);
        }

        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(payload);
        Tn3270EProtocol.TryParseHeader(span, out var dt, out var req, out var resp, out var seq);
        var data = span[TelnetConstants.Tn3270EHeaderSize..].ToArray();
        return new Tn3270EFrame(dt, req, resp, seq, data);
    }

    private static async Task<byte[]> ReadPlainRecordFromPipeAsync(TestEnvironment env)
    {
        var payload = new List<byte>();
        var expectEor = false;

        while (true)
        {
            var b = await env.ServerReadByteAsync();

            if (expectEor)
            {
                if (b == TelnetConstants.Eor)
                {
                    return [.. payload];
                }

                if (b == TelnetConstants.Iac)
                {
                    payload.Add(TelnetConstants.Iac);
                }

                expectEor = false;
                continue;
            }

            if (b == TelnetConstants.Iac)
            {
                expectEor = true;
                continue;
            }

            payload.Add(b);
        }
    }

    /// <summary>
    /// Writes a TN3270E frame into the server-to-client pipe for the service to receive.
    /// </summary>
    private static async Task WriteFrameToPipeAsync(
        TestEnvironment env,
        Tn3270EDataType dataType,
        byte req,
        byte resp,
        ushort seq,
        byte[] payload)
    {
        var header = Tn3270EProtocol.BuildHeader(dataType, req, resp, seq);
        var escaped = TelnetProtocol.EscapeIac(payload);
        var packet = new byte[header.Length + escaped.Length + 2];
        header.CopyTo(packet, 0);
        escaped.CopyTo(packet, header.Length);
        packet[^2] = TelnetConstants.Iac;
        packet[^1] = TelnetConstants.Eor;
        await env.ServerWriteAsync(packet);
    }

    // =========================================================================
    // TestEnvironment
    // =========================================================================

    /// <summary>
    /// Wires two <see cref="Pipe"/> instances so that bytes written by the service
    /// can be read by the test (and vice-versa).
    /// </summary>
    private sealed class TestEnvironment : IDisposable
    {
        // client→server: bytes the service writes, the test reads
        private readonly Pipe _clientToServer = new();
        // server→client: bytes the test writes, the service reads
        private readonly Pipe _serverToClient = new();

        public ITn3270EService Service { get; }

        public TestEnvironment()
        {
            var factory = new PipeNetworkConnectionFactory(
                _serverToClient.Reader.AsStream(),
                _clientToServer.Writer.AsStream());

            Service = new Tn3270EService(factory, NullLogger<Tn3270EService>.Instance);
        }

        // --- Server-side helpers ---

        public async Task ServerWriteAsync(byte[] data)
        {
            await _serverToClient.Writer.WriteAsync(data);
            await _serverToClient.Writer.FlushAsync();
        }

        public async Task<byte[]> ServerReadBytesAsync(int count)
        {
            var buf = new byte[count];
            var stream = _clientToServer.Reader.AsStream();
            var read = 0;
            while (read < count)
            {
                var n = await stream.ReadAsync(buf.AsMemory(read, count - read));
                if (n == 0)
                {
                    throw new EndOfStreamException();
                }

                read += n;
            }
            return buf;
        }

        public async Task<byte> ServerReadByteAsync()
        {
            var b = await ServerReadBytesAsync(1);
            return b[0];
        }

        /// <summary>Drains a client sub-negotiation up to and including IAC SE.</summary>
        public async Task ServerDrainSubnegotiationAsync()
        {
            while (true)
            {
                var b = await ServerReadByteAsync();
                if (b != TelnetConstants.Iac)
                {
                    continue;
                }

                var next = await ServerReadByteAsync();
                if (next == TelnetConstants.Se)
                {
                    return;
                }
            }
        }

        public async Task<(byte Option, byte[] Data)> ServerReadSubnegotiationAsync()
        {
            var prefix = await ServerReadBytesAsync(3);
            Assert.AreEqual(TelnetConstants.Iac, prefix[0]);
            Assert.AreEqual(TelnetConstants.Sb, prefix[1]);

            var option = prefix[2];
            var data = new List<byte>();

            while (true)
            {
                var b = await ServerReadByteAsync();
                if (b != TelnetConstants.Iac)
                {
                    data.Add(b);
                    continue;
                }

                var next = await ServerReadByteAsync();
                if (next == TelnetConstants.Se)
                {
                    return (option, [.. data]);
                }

                data.Add(TelnetConstants.Iac);
                if (next != TelnetConstants.Iac)
                {
                    data.Add(next);
                }
            }
        }

        public void Dispose()
        {
            _clientToServer.Writer.Complete();
            _clientToServer.Reader.Complete();
            _serverToClient.Writer.Complete();
            _serverToClient.Reader.Complete();
        }
    }

    /// <summary>
    /// An <see cref="INetworkConnectionFactory"/> that returns pre-constructed streams
    /// instead of opening a real TCP connection.
    /// </summary>
    private sealed class PipeNetworkConnectionFactory(
        Stream readStream,
        Stream writeStream) : INetworkConnectionFactory
    {
        public Task<Stream> ConnectAsync(
            string host, int port, CancellationToken cancellationToken)
        {
            Stream combined = new DuplexStream(readStream, writeStream);
            return Task.FromResult(combined);
        }
    }

    /// <summary>
    /// Combines separate read and write <see cref="Stream"/> instances into a single
    /// duplex stream that satisfies <see cref="Stream.CanRead"/> and <see cref="Stream.CanWrite"/>.
    /// </summary>
    private sealed class DuplexStream(Stream reader, Stream writer) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => writer.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) =>
            writer.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) =>
            reader.Read(buffer, offset, count);

        public override Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            reader.ReadAsync(buffer, offset, count, cancellationToken);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            reader.ReadAsync(buffer, cancellationToken);

        public override void Write(byte[] buffer, int offset, int count) =>
            writer.Write(buffer, offset, count);

        public override Task WriteAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            writer.WriteAsync(buffer, offset, count, cancellationToken);

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            writer.WriteAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();
    }
}
