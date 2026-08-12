using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Hero;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The client asking for the ballot, which is also the client asking to open it.
/// </summary>
/// <remarks>
/// No body: a bare request, answered for the asker's own nation. Every parameterless C2S type folds onto
/// one serializer, so an empty body here is identical-COMDAT folding rather than a base-class
/// fall-through.
///
/// This is what the Voting Machine sends. Clicking it produces this packet directly - no skill cast, no
/// CSStartInteraction - so the doodad's func never runs and the phase check has to live here.
///
/// The reply sets showUI. That was false at first, on the reasoning that a refresh should not re-raise
/// HERO_ELECTION and reopen a window already on screen; the reasoning was wrong. Nothing else asks for
/// this list, so the request IS the open, and answering with showUI false left the client holding a
/// perfectly good ballot it had no instruction to show.
/// </remarks>
public class CSHeroCandidateListPacket() : GamePacket(CSOffsets.CSHeroCandidateListPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var character = Connection?.ActiveChar;
        if (character == null)
            return;

        // Both phases open it: hero_abstain is when a candidate withdraws, and the withdrawal button
        // lives in this same window.
        var phase = HeroElectionManager.Instance.CurrentPhase;
        if (phase != HeroPhase.HeroVoting && phase != HeroPhase.HeroAbstain)
        {
            Logger.Debug("HeroCandidateList from {0} outside the election phases; ignored", character.Name);
            return;
        }

        HeroElectionManager.Instance.SendBallot(character, openWindow: true);
    }
}
