using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class OpenPortalEffect : EffectTemplate
    {
        public float Distance { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj,
            EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            var portalInfo = (SkillObjectUnk1)skillObject;
            var portalOwner = (Character)caster;
            // Private - [DEBUG] EffectTemplate - OpenPortalEffect, Owner: Lemes, PortalId: 4097, Type: 2, X: 20928, Y: 13145,9, Z:114,1004
            // District - [DEBUG] EffectTemplate - OpenPortalEffect, Owner: Lemes, PortalId: 3, Type: 1, X: 20921,96, Y: 13148,55, Z:114,2535
            _log.Debug("OpenPortalEffect, Owner: {0}, PortalId: {1}, Type: {5}, X: {2}, Y: {3}, Z:{4}", portalOwner.Name, portalInfo.Id, portalInfo.X, portalInfo.Y, portalInfo.Z, portalInfo.Type);

            if (portalInfo.X > portalOwner.Position.X + Distance || portalInfo.Y > portalOwner.Position.Y + Distance)
            {
                return;
            }
            PortalManager.Instance.OpenPortal(portalOwner, portalInfo); // TODO - Use Distance
        }
    }
}
