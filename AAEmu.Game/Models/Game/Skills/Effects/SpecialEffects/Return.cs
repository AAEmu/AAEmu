using System;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
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
            _log.Debug("value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);

            if (caster is Character character)
            {
                var ReturnPointId = value1;

                if (ReturnPointId == 0)
                {
                    //var portal = PortalManager.Instance.GetFavoritePortal();
                    var portal = PortalManager.Instance.GetPortalById(character.ReturnDictrictId);

                    character.DisabledSetPosition = true;
                    character.SendPacket(new SCTeleportUnitPacket(TeleportReason.MoveToLocation, 0, portal.X, portal.Y, portal.Z, portal.ZRot));
                }

                var trp = TeleportReturnPointGameData.Instance.GetTeleportReturnPoint((uint)ReturnPointId);

                if (trp == null && character.MainWorldPosition == null)
                {
                    return;
                }
                else if (ReturnPointId == 614 || character.MainWorldPosition != null)
                {
                    if (character.MainWorldPosition == null)
                    {
                        character.MainWorldPosition = character.Transform.CloneDetached(character);

                        character.MainWorldPosition.ZoneId = trp.UnitId;
                        var xyz = new Vector3(trp.Position.X, trp.Position.Y, trp.Position.Z);
                        character.MainWorldPosition.World.Position = xyz;
                        var rxyz = new Vector3(trp.Position.Roll, trp.Position.Pitch, trp.Position.Yaw);
                        character.MainWorldPosition.World.Rotation = rxyz;
                    }
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
                    caster.DisabledSetPosition = true;
                    caster.SendPacket(new SCTeleportUnitPacket(TeleportReason.MoveToLocation, 0, trp.Position.X, trp.Position.Y, trp.Position.Z, trp.Position.Yaw));
                }
            }
        }
    }
}
