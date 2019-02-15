using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class OpenPortalEffect : EffectTemplate
    {
        public float Distance { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj,
            Skill skill, SkillObject skillObject, DateTime time)
        {
            var portalInfo = (SkillObjectUnk1)skillObject;
            var portalOwner = (Character)caster;
            _log.Debug("OpenPortalEffect, Owner: {0}, PortalId: {1}", portalOwner.Name, portalInfo.Id);

            PortalManager.Instance.OpenPortal(portalOwner, portalInfo); // TODO - Use Distance
        }
    }
}
