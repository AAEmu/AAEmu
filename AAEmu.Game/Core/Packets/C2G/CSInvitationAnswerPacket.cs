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

        // The join dialog belongs to a battle field match the moment the character holds an invite
        // for one; without this branch the accept never reached the match and the ready stage was
        // unreachable. A dungeon invitee has no CurrentInstantGame and falls through to Indun.
        if (character.CurrentInstantGame is { } instantGame)
        {
            instantGame.PlayerInviteResponse(character, Acceptance, 0ul);
            return;
        }

        if (IndunMatchmakingManager.Instance.TryInvitationAnswer(character, InvitationTime, Acceptance))
            return;

        Logger.Debug("CSInvitationAnswer ignored char={0} acceptance={1}", character.Name, Acceptance);
    }
}
