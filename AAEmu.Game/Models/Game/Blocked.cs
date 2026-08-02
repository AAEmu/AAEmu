using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game;

public class Blocked : PacketMarshaler
{
    public uint CharacterId { get; set; }
    public string Name { get; set; }
    public sbyte WorldId { get; set; }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ulong)CharacterId);
        stream.Write(Name);
        stream.Write(WorldId);
        return stream;
    }
}
