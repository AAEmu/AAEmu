using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSJuryVerdictPacket() : GamePacket(CSOffsets.CSJuryVerdictPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var trial = stream.ReadUInt64();
        var jury = stream.ReadInt32();
        var sentence = stream.ReadByte();

        TrialManager.Instance.OnVerdict(Connection.ActiveChar, trial, jury, sentence);
    }
}
