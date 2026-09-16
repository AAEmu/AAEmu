using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Moves the caster to the resurrection point of its return district — the siege HQ the 진지로 이동 skills
/// (17046 and 20950) offer from inside a siege area.
/// </summary>
/// <remarks>
/// content, 10.0.2.13 game_decrypted: both skills read "공성 영역에서만 사용할 수 있고 사용 시 부활 지점으로
/// 이동합니다" (usable only in the siege area; moves you to the resurrection point), and both of their
/// <c>special_effects</c> rows carry no values at all, so the destination comes from the character and not
/// from the row. <c>MoveToRezPointEffect</c> resolves the same destination, and this effect lands the same
/// way, with <see cref="TeleportReason.Resurrect"/> rather than the unused ToSiegeDefenseHq /
/// ToSiegeOffenseHq reasons: the two skills are identical apart from their ids and icons, so which side each
/// one belongs to is not in the data.
/// </remarks>
public class TeleportToSiegeHq : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.TeleportToSiegeHq;

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

        // The resurrection point is the character's return district; PortalManager resolves it per faction.
        var returnPointId = PortalManager.Instance.GetDistrictReturnPoint(character.ReturnDistrictId);
        var portal = PortalManager.Instance.GetRespawnById(returnPointId);
        if (portal == null)
        {
            Logger.Warn($"TeleportToSiegeHq: no return point for {character.Name} (district {character.ReturnDistrictId})");
            return;
        }

        if (!TeleportToSiegeHqRules.CanTeleportTo(
                true,
                returnPointId,
                portal.ZoneId,
                WorldIntegration.ZoneAuthority,
                WorldIntegration.IsZoneLoaded))
        {
            // A destination nobody simulates would strand the character, exactly as with the house recall.
            Logger.Warn($"TeleportToSiegeHq: refusing to move {character.Name} to return point {returnPointId} in zone {portal.ZoneId}; no ZoneLoaded dedicate");
            return;
        }

        var landingWorldId = ReturnTeleportRules.LoadWorldId(portal.WorldId, WorldManager.DefaultWorldTemplateId);
        Logger.Info("TeleportToSiegeHq: moving {0} to return point {1} ({2:0.0}, {3:0.0}, {4:0.0})",
            character.Name, returnPointId, portal.X, portal.Y, portal.Z);

        // Respawn points live in the main world, so the landing never crosses an instance, and dying and
        // resurrecting in the same zone is not a zone change either.
        SkillTeleportLanding.Apply(character, landingWorldId, portal.ZoneId, WorldManager.DefaultInstanceId,
            portal.X, portal.Y, portal.Z, 0f, TeleportReason.Resurrect,
            portal.ZoneId == character.Transform.ZoneId &&
            WorldManager.DefaultInstanceId == character.Transform.InstanceId);
    }
}
