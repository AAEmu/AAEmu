using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSDelegateSquadLeaderPacket() : GamePacket(CSOffsets.CSDelegateSquadLeaderPacket, 1)
{
    public long WorldCharKey { get; private set; }

    public override void Read(PacketStream stream)
    {
        WorldCharKey = stream.ReadInt64();
        SquadManager.Instance.DelegateLeader(Connection.ActiveChar, (ulong)WorldCharKey);
    }
}
