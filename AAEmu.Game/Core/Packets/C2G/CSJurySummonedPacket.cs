using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSJurySummonedPacket() : GamePacket(CSOffsets.CSJurySummonedPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var trial = stream.ReadUInt64();
        var court = stream.ReadInt32();
        var jury = stream.ReadInt32();

        TrialManager.Instance.OnSummoned(Connection.ActiveChar, trial, court, jury);
    }
}
