using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// 170 zone_conflict_change: declares a new war state for the zone group the caster stands in.
/// </summary>
/// <remarks>
/// 19 skills carry this on 22 rows: 39730/41199 전쟁 선포 (War), 39746/41198 분쟁 선포 (Conflict),
/// 39747/40759/41196 평화 선포 (Peace) and 39749/41197 위험 선포 (Tension). The state is value4; see
/// <see cref="ZoneConflictChangeRules"/>. The declaration lands on the caster's own zone group — the same
/// resolution the <c>/testzonestate</c> command uses — and goes out through
/// <see cref="ZoneConflict.SetState"/>, which publishes SCConflictZoneStatePacket to everyone in the
/// group and relays the state to the zone hosts.
/// <para>
/// <c>value3</c> (5400 s on most rows, i.e. 90 minutes) is the duration the declaration asks for. It is
/// not applied: a zone group's per-state length is its cycle configuration (<c>WarMin</c>/<c>PeaceMin</c>,
/// reloaded from the conflict row at boot), and writing a cast's duration into it would outlive the
/// declaration and change every later cycle of that zone. The state change is applied with the
/// configured length instead.
/// </para>
/// </remarks>
public class ZoneConflictChange : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ZoneConflictChange;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (caster is not Character character)
            return;

        var requested = ZoneConflictChangeRules.ResolveState(value4);
        if (requested == null)
        {
            Logger.Warn("zone_conflict_change: value4={0} is not a known war state", value4);
            return;
        }

        var zone = ZoneManager.Instance.GetZoneByKey(character.Transform.ZoneId);
        var zoneGroup = zone == null ? null : ZoneManager.Instance.GetZoneGroupById(zone.GroupId);
        var conflict = zoneGroup?.Conflict;
        if (conflict == null)
        {
            Logger.Debug(
                "zone_conflict_change: zone group {0} has no conflict state in the database",
                zone?.GroupId ?? 0);
            return;
        }

        if (!ZoneConflictChangeRules.IsChange(conflict.CurrentZoneState, requested.Value))
            return;

        conflict.SetState(requested.Value);
    }
}
