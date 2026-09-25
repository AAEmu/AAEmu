using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

/// <summary>
/// Opens the client resident townhall from a content-backed public doodad function and sends the
/// current persisted zone-group state that the window consumes.
/// </summary>
public sealed class DoodadFuncResidentTownhallUiOpen : DoodadFuncTemplate
{
    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        if (caster is not Character character || character.Connection == null)
            return;

        TryOpen(character, owner, ZoneManager.Instance,
            (actor, zoneGroup) => HousingManager.Instance.ResidentInfo(actor.Connection, zoneGroup));
    }

    internal static bool TryOpen(Character character, Doodad owner, IZoneManager zoneManager,
        Action<Character, short> open)
    {
        if (character == null || owner?.Transform == null || zoneManager == null || open == null)
            return false;

        var groupId = zoneManager.GetZoneByKey(owner.Transform.ZoneId)?.GroupId ?? 0u;
        if (groupId == 0 || groupId > short.MaxValue)
            return false;

        open(character, (short)groupId);
        return true;
    }
}
