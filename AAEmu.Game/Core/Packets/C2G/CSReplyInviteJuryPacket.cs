using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSReplyInviteJuryPacket() : GamePacket(CSOffsets.CSReplyInviteJuryPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var accept = stream.ReadBoolean();
        var trial = stream.ReadUInt32();

        Logger.Debug($"ReplyInviteJury, Accept: {accept}, Trial: {trial}");
        if (!TrialManager.Instance.ProcessTrialInviteReply(Connection.ActiveChar, accept, trial))
        {
            // Error messages for the client are already sent by the function
            // If we can't join, it's likely that the jury is already full
            Logger.Debug($"{Connection.ActiveChar.Name} could not join trial: {trial}");
        }
    }
}
