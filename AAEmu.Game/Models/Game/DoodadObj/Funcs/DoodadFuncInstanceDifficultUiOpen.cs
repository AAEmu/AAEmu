using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncInstanceDifficultUiOpen : DoodadFuncTemplate
{
    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        if (caster is not Character character || owner == null)
            return;

        if (!HasPermission(character, owner.FuncPermission, TeamManager.Instance.GetActiveTeamByUnit))
            return;

        var world = character.ParentWorld;
        var dungeon = world?.DungeonInstance;
        if (dungeon == null || owner.ParentWorld != world ||
            !dungeon.BeginDifficultySelection(character,
                () => owner.DoChangePhase(character, nextPhase)))
            return;

        character.SendPacket(new SCSelectedInstanceDifficultPacket((sbyte)(dungeon.Difficult ?? 0), showUi: true));
    }

    /// <summary>
    /// Only PUBLIC opens the picker. Function 44982 on doodad 17113 is PARTY_OWNER, so a party member who
    /// is not the owner is refused here and never takes the copy's difficulty reservation from the leader.
    /// </summary>
    internal static bool HasPermission(Character character, DoodadFuncPermission permission,
        Func<uint, Team.Team> teamByUnitId)
    {
        if (character == null)
            return false;

        if (permission == DoodadFuncPermission.Public)
            return true;

        var team = teamByUnitId?.Invoke(character.Id);
        if (permission == DoodadFuncPermission.PartyOwner && team is { IsParty: true } &&
            team.OwnerId == character.Id)
            return true;

        character.SendErrorMessage(ErrorMessageType.InteractionPermissionDeny);
        return false;
    }
}
