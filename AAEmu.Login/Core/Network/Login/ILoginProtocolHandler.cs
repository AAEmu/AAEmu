using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using AAEmu.Commons.Network;

namespace AAEmu.Login.Core.Network.Login;

public interface ILoginProtocolHandler
{
    /// <summary>
    /// Attempts to parse a single packet from the provided buffer.
    /// </summary>
    /// <param name="buffer">The sequence of bytes that are available.</param>
    /// <param name="packetType">When this method returns, contains the type of the packet that was parsed.</param>
    /// <param name="packet">When this method returns, contains the packet data that was parsed.</param>
    /// <returns>true if a packet was successfully parsed; otherwise, false.</returns>
    /// <remarks>
    /// <paramref name="buffer"/> must be mutated by this method; if a packet was parsed, the start of the buffer must
    /// point to the end of the parsed packet, and the end of the buffer must point to the end of the original buffer.
    /// If a packet was not parsed, the buffer must remain unchanged.
    /// </remarks>
    bool TryParsePacket(ref ReadOnlySequence<byte> buffer, out ushort packetType,
        [NotNullWhen(true)] out PacketStream? packet);
}
