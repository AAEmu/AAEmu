using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

using WorldIntegration = AAEmu.Game.WorldIntegration;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Heals the caster's pet by a percentage of its maximum health. The 소환수 부상 치료 물약 items carry it
/// (skills 15220, 40302, 40447, 49699, 49488, 49726), and those are used by the owner, not by the summon.
/// </summary>
/// <remarks>
/// content, 10.0.2.13 game_decrypted: 9 <c>special_effects</c> rows of type 56, 7 of them reachable, all
/// through <c>skill_effects</c> and all on <c>skills.target_type_id</c> 9 (item) with
/// <c>target_selection_id</c> 1 (source) — the potion is the target, so the summon has to be looked up
/// rather than read off the effect's target. The two value slots are the same number on every shipped row
/// (10/10 on 66461, 20/20 on 3461 / 39276 / 39614, 50/50 on the rest), so they are one percentage band
/// rather than a range. The potion text also claims 활력 (vigor) is restored, but no slot carries a second
/// band, so only health is healed here.
/// </remarks>
public class HealPet : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.HealPet;

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
        if (caster?.ParentWorld == null || caster.Id == 0)
            return;

        var (min, max) = PetHealRules.PercentBand(value1, value2);
        if (max <= 0)
            return;

        // The potion targets its user, so the summon has to be looked up. The codebase treats the first
        // active mate as "the pet" everywhere else (SkillTargetingUtil, Blink's dismount).
        var pet = caster.ParentWorld.MateManager.GetActiveMates(caster.Id).FirstOrDefault();
        if (pet == null || pet.Hp <= 0)
            return;

        var percent = min == max ? min : Random.Shared.Next(min, max + 1);
        var value = PetHealRules.AmountFor(percent, pet.MaxHp);
        if (value <= 0)
            return;

        var healed = Math.Min(value, pet.MaxHp - pet.Hp);
        if (healed <= 0)
            return;

        pet.Hp += healed;
        // Same wire pair a HealEffect ends with: the heal number, then the pet's refreshed bars.
        pet.BroadcastPacket(
            new SCUnitHealedPacket(castObj, casterObj, pet.ObjId, HealType.Health, HealHitType.HealHit, healed), true);
        pet.BroadcastPacket(new SCUnitPointsPacket(pet.ObjId, pet.Hp, pet.Mp), true);

        if (WorldIntegration.ZoneAuthority)
        {
            var inCharge = caster is Unit unit && unit.ObjId != 0 ? unit.ObjId : pet.ObjId;
            WorldIntegration.RelayUnitHealedToZone?.Invoke(
                castObj, casterObj, pet.ObjId, HealType.Health, HealHitType.HealHit, healed, inCharge, false);
            WorldIntegration.RelayUnitPointsToZone?.Invoke(pet.ObjId, pet.Hp, pet.Mp);
        }
    }
}
