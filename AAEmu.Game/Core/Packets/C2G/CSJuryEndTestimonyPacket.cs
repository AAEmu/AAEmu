using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSJuryEndTestimonyPacket() : GamePacket(CSOffsets.CSJuryEndTestimonyPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var trial = stream.ReadUInt64();
        var jury = stream.ReadInt32();

        TrialManager.Instance.OnEndTestimony(Connection.ActiveChar, trial, jury);
    }
}
