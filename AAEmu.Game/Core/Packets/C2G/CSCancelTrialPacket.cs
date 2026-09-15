using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCancelTrialPacket() : GamePacket(CSOffsets.CSCancelTrialPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var trial = stream.ReadUInt64();
        TrialManager.Instance.OnCancel(Connection.ActiveChar, trial);
    }
}
