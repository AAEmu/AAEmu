using AAEmu.Commons.Network;

namespace AAEmu.World.Core.Packets;

/// <summary>
/// Base for WZ/ZW frames: [u16 length][u16 opcode][body].
/// Length covers opcode + body (not the length field itself).
/// </summary>
public abstract class ZonePacket(ushort opcode)
{
    public ushort Opcode { get; } = opcode;

    protected abstract void WriteBody(PacketStream stream);

    public byte[] Encode()
    {
        var body = new PacketStream();
        WriteBody(body);

        var frame = new PacketStream();
        frame.Write(Opcode);
        frame.Write(body, false);

        var outer = new PacketStream();
        outer.Write(frame);
        return outer.GetBytes();
    }
}
