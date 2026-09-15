using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

public class MoveToRezPointEffect : EffectTemplate
{
    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (target is not Char.Character character)
            return;

        // The resurrection point is the character's return district; PortalManager resolves it per faction.
        var returnPointId = Core.Managers.PortalManager.Instance.GetDistrictReturnPoint(character.ReturnDistrictId);
        var portal = Core.Managers.PortalManager.Instance.GetRespawnById(returnPointId);
        if (portal == null)
        {
            Logger.Warn($"MoveToRezPointEffect: no return point for {character.Name} (district {character.ReturnDistrictId})");
            return;
        }

        // A destination nobody simulates would strand the character, exactly as with the house recall.
        if (!TeleportLandingRules.CanLandInZone(
                WorldIntegration.ZoneAuthority, WorldIntegration.IsZoneLoaded, portal.ZoneId))
        {
            Logger.Warn($"MoveToRezPointEffect: refusing to move {character.Name} to return point in zone {portal.ZoneId}; no ZoneLoaded dedicate");
            return;
        }

        var landingWorldId = ReturnTeleportRules.LoadWorldId(portal.WorldId, WorldManager.DefaultWorldTemplateId);
        Logger.Info("MoveToRezPointEffect: moving {0} to return point {1} ({2:0.0}, {3:0.0}, {4:0.0})",
            character.Name, returnPointId, portal.X, portal.Y, portal.Z);

        // Respawn points live in the main world, so the landing never crosses an instance.
        SkillTeleportLanding.Apply(character, landingWorldId, portal.ZoneId, WorldManager.DefaultInstanceId,
            portal.X, portal.Y, portal.Z, 0f, TeleportReason.Resurrect,
            // Dying and resurrecting in the same zone is not a zone change either.
            portal.ZoneId == character.Transform.ZoneId &&
            WorldManager.DefaultInstanceId == character.Transform.InstanceId);
    }
}
