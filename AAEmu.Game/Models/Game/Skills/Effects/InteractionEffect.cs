using System;

using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class InteractionEffect : EffectTemplate
    {
        public WorldInteractionType WorldInteraction { get; set; }
        public uint DoodadId { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
            CompressedGamePackets packetBuilder = null)
        {
            _log.Debug("InteractionEffect, {0}", WorldInteraction);

            var classType = Type.GetType("AAEmu.Game.Models.Game.World.Interactions." + WorldInteraction);
            if (classType == null)
            {
                _log.Error("InteractionEffect, Unknown world interaction: {0}", WorldInteraction);
                return;
            }

            _log.Debug("InteractionEffect, Action: {0}", classType); // TODO help to debug...

            caster.Buffs.TriggerRemoveOn(Buffs.BuffRemoveOn.Interaction);

            var action = (IWorldInteraction)Activator.CreateInstance(classType);
            if (source is {Skill: { }} && casterObj != null && target != null && targetObj != null && source.Skill.Template != null)
            {
                action?.Execute(caster, casterObj, target, targetObj, source.Skill.Template.Id, DoodadId);
            }

            if (caster is not Character character) { return; }
            if (target is Doodad)
            {
                character.Quests.OnInteraction(WorldInteraction, target);
            }
        }
    }
}
