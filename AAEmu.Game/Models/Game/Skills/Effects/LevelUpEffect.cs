using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// Raises the target to a level, used by the blessing and growth-stone skills (32366 키리오스의 축복,
/// 38998, 47152, 48327). Every shipped row names level 55.
/// </summary>
public class LevelUpEffect : EffectTemplate
{
    public int Level { get; set; }
    public bool ApplyAllAbilities { get; set; }

    public override bool OnActionTime => false;

    public override void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (target is not Character character)
            return;

        // Only ever a promotion; these skills must not be able to strip levels off a higher character.
        if (Level <= character.Level)
            return;

        if (Level > ExperienceManager.Instance.MaxPlayerLevel)
            return;

        Logger.Debug($"LevelUpEffect: {character.Name} {character.Level} -> {Level}, applyAllAbilities {ApplyAllAbilities}");

        // Level is a function of experience here, so granting it means topping the character's exp up to the
        // threshold - the same route the ChangeLevel command takes. apply_all_abilities carries every equipped
        // ability up with the character rather than only the active one, which is what AddExp's flag does.
        var expToAdd = ExperienceManager.Instance.GetExpNeededToGivenLevel(character.Experience, (byte)Level);

        if (ApplyAllAbilities)
        {
            foreach (var ability in new[] { character.Ability1, character.Ability2, character.Ability3 })
            {
                if (ability == AbilityType.None)
                    continue;

                var expForAbility = ExperienceManager.Instance.GetExpNeededToGivenLevel(
                    character.Abilities.Abilities[ability].Exp, (byte)Level);
                if (expForAbility > expToAdd)
                    expToAdd = expForAbility;
            }
        }

        if (expToAdd > 0)
            character.AddExp(expToAdd, ApplyAllAbilities);
        // Full vitals + SCUnitPoints / UnitState / zone points are applied inside Character.AddExp
        // when Level actually rises (ApplyLevelUpBenefits). Re-heal here only if already at target
        // (AddExp no-ops when exp needed is 0).
        else if (character.Hp < character.MaxHp || character.Mp < character.MaxMp)
        {
            character.Hp = character.MaxHp;
            character.Mp = character.MaxMp;
            character.BroadcastPacket(new SCUnitPointsPacket(character.ObjId, character.Hp, character.Mp), true);
        }
    }
}
