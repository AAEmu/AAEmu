using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSChangeSquadMemberRolePacket() : GamePacket(CSOffsets.CSChangeSquadMemberRolePacket, 1)
{
    public sbyte Role { get; private set; }

    public override void Read(PacketStream stream)
    {
        Role = stream.ReadSByte();
        SquadManager.Instance.ChangeRole(Connection.ActiveChar, Role);
    }
}
