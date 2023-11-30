using System;

using AAEmu.Game.Core.Managers;
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

        // проверяем сначала на запись в книге возвратов
        if (value1 == 0)
        {
            // Memory Tome for Recall skill
            returnPointId = PortalManager.Instance.GetDistrictReturnPoint(character.ReturnDistrictId, character.Faction.Id);
            trp = PortalManager.Instance.GetRecallById(returnPointId);
            if (returnPointId == 0) { return; }
        }
        else
        {
            // Worldgates
            returnPointId = (uint)value1;
            trp = PortalManager.Instance.GetWorldgatesById(returnPointId);
        }

        if (trp != null)
        {
            character.DisabledSetPosition = true;
            character.SendPacket(
                new SCLoadInstancePacket(
                    1,
                    trp.ZoneId,
                    trp.X,
                    trp.Y,
                    trp.Z,
                    0,
                    0,
                    trp.Yaw.DegToRad()
                )
            );
            //var xyz = new Vector3(trp.X, trp.Y, trp.Z);
            //character.Transform = new Transform(character, null, xyz);
            // возвращаемся в main_world
            character.Transform = new Transform(character, null, 0, trp.ZoneId, 0, trp.X, trp.Y, trp.Z, trp.Yaw.DegToRad());
            character.MainWorldPosition = null;
        }
        else if (character.MainWorldPosition != null)
        {
            character.DisabledSetPosition = true;
            character.SendPacket(
                new SCLoadInstancePacket(
                    1,
                    character.MainWorldPosition.ZoneId,
                    character.MainWorldPosition.World.Position.X,
                    character.MainWorldPosition.World.Position.Y,
                    character.MainWorldPosition.World.Position.Z,
                    character.MainWorldPosition.World.Rotation.X,
                    character.MainWorldPosition.World.Rotation.Y,
                    character.MainWorldPosition.World.Rotation.Z
                )
            );

            character.Transform = character.MainWorldPosition.Clone(character);
            character.MainWorldPosition = null;
        }

        if (trp == null)
        {
            Logger.Info($"Return: Требуется добавить информацию в файл worldgates.json:\r\n" +
                        $"        \"Id\": {value1}\r\n" +
                        $"        \"ZoneId\": {character.Transform.ZoneId},\r\n" +
                        $"        \"X\": {character.Transform.World.Position.X},\r\n" +
                        $"        \"Y\": {character.Transform.World.Position.Y},\r\n" +
                        $"        \"Z\": {character.Transform.World.Position.Z},\r\n" +
                        $"        \"Yaw\": {character.Transform.World.Rotation.Z},\r\n" +
                        $"        \"SubZoneId\": {character.SubZoneId}\r\n" +
                        $"Координаты надо поставить правильные, эти для примера.");
            return;
        }

        caster.DisabledSetPosition = true;
        character.SendPacket(new SCTeleportUnitPacket(TeleportReason.MoveToLocation, 0, trp.X, trp.Y, trp.Z, trp.Yaw.DegToRad()));
    }
}
