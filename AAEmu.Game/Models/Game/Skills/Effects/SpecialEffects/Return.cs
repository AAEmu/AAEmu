using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Teleport;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class Return : SpecialEffectAction
{
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
        // TODO ...
        if (caster is Character) { Logger.Info("Special effects: Return value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4); }

        if (caster is not Character character) { return; }
        uint returnPointId;
        Portal trp;

        // first check for an entry in the return book
        if (value1 == 0)
        {
            // Memory Tome for Recall skill
            returnPointId = PortalManager.Instance.GetDistrictReturnPoint(character.ReturnDistrictId, character.Faction.Id);
            trp = PortalManager.Instance.GetRecallById(returnPointId);
            if (returnPointId == 0) { return; }
        }
        else
        {
            // value1 is return_points.id — worldgate / recall JSON, then return_point.g.
            returnPointId = (uint)value1;
            trp = PortalManager.Instance.GetReturnPoint(returnPointId);
        }

        if (trp != null)
        {
            if (!ReturnTeleportRules.HasValidDestination(trp.X, trp.Y, trp.Z))
            {
                Logger.Warn("Return refused origin dest point={0}", returnPointId);
                return;
            }

            ApplyReturn(
                character,
                ReturnTeleportRules.LoadWorldId(trp.WorldId, WorldManager.DefaultWorldTemplateId),
                WorldManager.DefaultInstanceId,
                trp.ZoneId,
                trp.X,
                trp.Y,
                trp.Z,
                trp.Yaw.DegToRad());
            return;
        }

        if (character.MainWorldPosition != null)
        {
            var home = character.MainWorldPosition.World.Position;
            if (!ReturnTeleportRules.HasValidDestination(home.X, home.Y, home.Z))
            {
                Logger.Warn("Return refused origin MainWorldPosition for {0}", character.Name);
                return;
            }

            var homeRot = character.MainWorldPosition.World.Rotation;
            ApplyReturn(
                character,
                ReturnTeleportRules.LoadWorldId(character.MainWorldPosition.WorldId, WorldManager.DefaultWorldTemplateId),
                character.MainWorldPosition.InstanceId,
                character.MainWorldPosition.ZoneId,
                home.X,
                home.Y,
                home.Z,
                homeRot.Z.DegToRad());
            return;
        }

        Logger.Info($"Return: Need to add information to worldgates.json:\r\n" +
                    $"        \"Id\": {value1}\r\n" +
                    $"        \"ZoneId\": {character.Transform.ZoneId},\r\n" +
                    $"        \"X\": {character.Transform.World.Position.X},\r\n" +
                    $"        \"Y\": {character.Transform.World.Position.Y},\r\n" +
                    $"        \"Z\": {character.Transform.World.Position.Z},\r\n" +
                    $"        \"Yaw\": {character.Transform.World.Rotation.Z},\r\n" +
                    $"        \"SubZoneId\": {character.SubZoneId}\r\n" +
                    $"The coordinates need to be set correctly, these are just an example.");
    }

    private static void ApplyReturn(
        Character character,
        uint destWorldId,
        uint destInstanceId,
        uint zoneId,
        float x,
        float y,
        float z,
        float yawRad)
    {
        // Shared with the house recall and the return-to-rez-point effects so every skill-driven
        // teleport lands the same way on the server and the client. A landing inside the zone the
        // character already occupies must not re-resolve the zone from its coordinates - only a real
        // cross-zone return goes through the handoff.
        var stayInZone = zoneId == character.Transform.ZoneId &&
                         destInstanceId == character.Transform.InstanceId;
        SkillTeleportLanding.Apply(character, destWorldId, zoneId, destInstanceId, x, y, z, yawRad,
            TeleportReason.MoveToLocation, stayInZone);
    }
}
