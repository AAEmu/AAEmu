using AAEmu.Commons.Network;

namespace AAEmu.UnitTests.Utils;

/// <summary>Reads one captured S→C game packet back into its opcode and body.</summary>
public static class SentPacket
{
    /// <summary>One captured packet: its opcode and its body, past the unencrypted game envelope.</summary>
    public static (ushort Opcode, byte[] Body) Read(IReadOnlyList<byte> packet)
    {
        var stream = new PacketStream(packet.ToArray());
        stream.ReadUInt16(); // envelope length
        stream.ReadByte();   // signature
        var level = stream.ReadByte();
        if (level == 1)
        {
            stream.ReadByte(); // checksum
            stream.ReadByte(); // counter
        }

        return (stream.ReadUInt16(), stream.ReadBytes(stream.LeftBytes));
    }

    public static PacketStream Body(IReadOnlyList<byte> packet) => new(Read(packet).Body);
}
