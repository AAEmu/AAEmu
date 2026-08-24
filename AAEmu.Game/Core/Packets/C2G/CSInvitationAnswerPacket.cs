using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSInvitationAnswerPacket() : GamePacket(CSOffsets.CSInvitationAnswerPacket, 1)
{
    public int InvitationTime { get; private set; }
    public bool Acceptance { get; private set; }

    public override void Read(PacketStream stream)
    {
        InvitationTime = stream.ReadInt32();
        Acceptance = stream.ReadBoolean();

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        if (!IndunMatchmakingManager.Instance.TryInvitationAnswer(character, InvitationTime, Acceptance))
            Logger.Debug("CSInvitationAnswer ignored char={0} acceptance={1}", character.Name, Acceptance);
    }
}
