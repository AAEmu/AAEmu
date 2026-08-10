using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Hero;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>
/// The Voting Machine - doodads 9421 and 9455, "당신의 소중한 한 표", one per nation's headquarters.
/// </summary>
/// <remarks>
/// Its data table, doodad_func_hero_elections, has nothing in it but an id, so the func carries no
/// configuration at all: interacting with it means "open the ballot", and every rule about whether that
/// is allowed lives on the server.
///
/// The window itself is client-side and already built (x2ui/hero/election.lua). It opens on the
/// HERO_ELECTION event and closes on INTERACTION_END, which is why walking away from the machine shuts
/// it - and why this is an interaction rather than a menu.
/// </remarks>
public class DoodadFuncHeroElection : DoodadFuncTemplate
{
    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        if (caster is not Character character)
            return;

        var phase = HeroElectionManager.Instance.CurrentPhase;
        if (phase != HeroPhase.HeroVoting && phase != HeroPhase.HeroAbstain)
        {
            // The client refuses first when it knows the phase, so this is the case where our state and
            // its state disagree - worth saying out loud rather than doing nothing.
            character.SendMessage("The hero election is not open.");
            return;
        }

        var nation = HeroManager.NationOf(character);
        if (nation == 0)
        {
            character.SendMessage("You have no nation to vote in.");
            return;
        }

        HeroElectionManager.Instance.SendBallot(character, openWindow: true);
    }
}
