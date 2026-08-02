using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
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
        if (WorldIntegration.ZoneAuthority
            && WorldIntegration.IsZoneLoaded != null
            && !WorldIntegration.IsZoneLoaded(portal.ZoneId))
        {
            Logger.Warn($"MoveToRezPointEffect: refusing to move {character.Name} to return point in zone {portal.ZoneId}; no ZoneLoaded dedicate");
            return;
        }

        Logger.Debug($"MoveToRezPointEffect: moving {character.Name} to return point {returnPointId}");

        character.DisabledSetPosition = true;
        character.SendPacket(new Core.Packets.G2C.SCTeleportUnitPacket(0, 0, portal.X, portal.Y, portal.Z, 0f));
    }
}
