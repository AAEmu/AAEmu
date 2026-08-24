using AAEmu.Commons.Network;

namespace AAEmu.Game.Core.Packets;

/// <summary>
/// A payload the client parses in a second pass, out of a byte buffer nested inside the packet.
/// Used by the squad list/create/leave packets and by the instant-game invitation.
///
/// The client's buffer already carries a 4-byte prologue of its own — a u16 length covering
/// everything after it, then a u16 tag — and its deferred read cursor starts at offset 4. The
/// buffer limit is derived as <c>buffer[0..1] + 2</c>, so the length word must describe
/// <c>tag + payload</c>, not the payload alone.
///
/// Wire:
///   u16 blobSize   = payload + 4   (transport size)
///   u16 blobSize   = payload + 4   (repeated; bounded by the first)
///   u16 innerSize  = payload + 2   (tag + payload, becomes the reader's limit)
///   u16 tag        = 0
///   payload[payload]
///
/// Getting the header short (or the values off by the prologue) leaves the deferred reader with a
/// limit below its start cursor, which the client answers with a throw and exits.
/// </summary>
public static class NestedBlobWire
{
    /// <summary>u16 blobSize + u16 blobSize + u16 innerSize + u16 tag.</summary>
    public const int HeaderSize = 8;

    /// <summary>Bytes the client's buffer already accounts for before the payload.</summary>
    private const int PrologueSize = 4;

    /// <summary>Client clamps its buffer growth to 65000 bytes.</summary>
    private const int MaxBlobSize = 65000;

    private const ushort Tag = 0;

    public static void Write(PacketStream stream, PacketStream payload)
    {
        var payloadBytes = payload?.GetBytes() ?? [];
        var blobSize = payloadBytes.Length + PrologueSize;
        if (blobSize > MaxBlobSize)
            throw new InvalidOperationException(
                $"Nested payload too large ({payloadBytes.Length} bytes, max {MaxBlobSize - PrologueSize})");

        stream.Write((ushort)blobSize);
        stream.Write((ushort)blobSize);
        stream.Write((ushort)(payloadBytes.Length + 2));
        stream.Write(Tag);
        if (payloadBytes.Length > 0)
            stream.Write(payloadBytes, appendSize: false);
    }

    /// <summary>
    /// Reproduces a default-constructed client buffer, whose cursor and limit both sit at 4.
    /// </summary>
    public static void WriteEmpty(PacketStream stream) => Write(stream, new PacketStream());
}
