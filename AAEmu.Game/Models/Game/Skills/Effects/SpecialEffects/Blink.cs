using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class Blink : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.Blink;

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
        if (caster is Character)
            Logger.Debug($"Special effects: Blink value1 {value1}, value2 {value2}, value3 {value3}, value4 {value4}");

        if (caster is not Character character)
            return;

        // value1 is the travel distance in metres; value2 is the direction offset in degrees, which is
        // what the side-step (-90/90) and mirror blinks use. Compute the landing with the same rotation
        // the client is told about, or the two disagree and the zone corrects the character back.
        var start = character.Transform.World.Position;
        var yawDegrees = character.Transform.World.ToRollPitchYawDegrees().Z;
        var (destX, destY) = BlinkLandingRules.GetLanding(start.X, start.Y, yawDegrees, value1, value2);

        if (character.IsRiding)
        {
            var mateList = character.ParentWorld.MateManager.GetActiveMates(character.Id);
            foreach (var mate in mateList)
                character.ParentWorld.MateManager.UnMountMate(character, mate.TlId, AttachPointKind.Driver, AttachUnitReason.None);
        }

        SkillBlinkLanding.Apply(character, destX, destY, start.Z, value1, value2, value3 != 0);
        skill?.NoteCasterSoftMoved();
    }
}
