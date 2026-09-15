using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSReplyInviteJuryPacket() : GamePacket(CSOffsets.CSReplyInviteJuryPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var accept = stream.ReadBoolean();
        var trial = stream.ReadUInt64();

        TrialManager.Instance.OnReplyInvite(Connection.ActiveChar, accept, trial);
    }
}
