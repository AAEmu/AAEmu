using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class Return : SpecialEffectAction
    {
        public override void Execute(Unit caster,
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
            _log.Debug("Return: value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);

            if (caster is not Character character) { return; }
            uint returnPointId;

            // проверяем сначала на запись в книге возвратов
            if (value1 == 0)
            {
                returnPointId = PortalManager.Instance.GetDistrictReturnPoint(character.ReturnDictrictId, character.Faction.Id);
                if (returnPointId == 0) { return; }
            }
            else
            {
                returnPointId = (uint)value1;
            }

            var trp = PortalManager.Instance.GetPortalById(returnPointId);
            if (trp == null && character.MainWorldPosition == null) { return; }

            // проверим что возвращаемся из Библиотеки (она инстанс) или если character.MainWorldPosition != null, то любой инстанс
            if (character.MainWorldPosition != null)
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
            else
            {
                if (trp == null) { return; }

                caster.DisabledSetPosition = true;
                caster.SendPacket(new SCTeleportUnitPacket(TeleportReason.MoveToLocation, 0, trp.X, trp.Y, trp.Z, trp.Yaw));
            }
        }
    }
}
