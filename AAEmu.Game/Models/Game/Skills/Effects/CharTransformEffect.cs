using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

// char_transform_effects — toggles a visual race/gender transform on the unit (costume/polymorph forms).
public class CharTransformEffect : EffectTemplate
{
    public int CharRaceId { get; set; }
    public int CharGenderId { get; set; }
    public bool IsTransform { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (target is not Char.Character character)
            return;

        if (IsTransform)
        {
            // Remember what to come back to the first time a transform is applied, so a second one stacking on
            // top cannot make the original body unrecoverable.
            character.PreTransformModelId ??= character.ModelId;

            var form = Core.Managers.UnitManagers.CharacterManager.Instance
                .GetTemplate((Char.Race)CharRaceId, (Char.Gender)CharGenderId);
            if (form != null)
                character.ModelId = form.ModelId;
        }
        else if (character.PreTransformModelId.HasValue)
        {
            character.ModelId = character.PreTransformModelId.Value;
            character.PreTransformModelId = null;
        }

        Logger.Debug($"CharTransformEffect: {character.Name} transform {IsTransform} race {CharRaceId} gender {CharGenderId} -> model {character.ModelId}");

        // SCUnitState carries the model, so re-sending the unit is what makes onlookers see the new body.
        character.BroadcastPacket(new Core.Packets.G2C.SCUnitStatePacket(character), true);
    }
}
