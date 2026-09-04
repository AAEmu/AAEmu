using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSExpelSquadMemberPacket() : GamePacket(CSOffsets.CSExpelSquadMemberPacket, 1)
{
    public ulong TypeValue { get; private set; }

    public override void Read(PacketStream stream)
    {
        TypeValue = stream.ReadUInt64();
        SquadManager.Instance.Expel(Connection.ActiveChar, TypeValue);
    }
}
