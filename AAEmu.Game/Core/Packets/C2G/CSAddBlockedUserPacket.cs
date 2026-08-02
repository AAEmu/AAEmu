using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// </summary>
public class CSAddBlockedUserPacket() : GamePacket(CSOffsets.CSAddBlockedUserPacket, 1)
{
    public string Name { get; private set; }
    public sbyte WorldId { get; private set; }

    public override void Read(PacketStream stream)
    {
        Name = stream.ReadString();
        WorldId = stream.ReadSByte();
    }

    public override void Execute()
    {
        Connection.ActiveChar.Blocked.AddBlockedUser(Name, WorldId);
    }
}
