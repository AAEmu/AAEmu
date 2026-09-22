using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Special effect type 101. No 10.0.2.13 special_effects row carries it (the enum table skips 101 and
/// the in-game shop enters the salon through CSBeautyshopBypass instead), so this only runs for
/// custom content. It opens the same session the bypass packet does, so CSBeautyshopData is honoured.
/// </summary>
public class EnterBeautyshop : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.EnterBeautyshop;

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
        if (caster is Character player)
        {
            Logger.Debug("Special effects: EnterBeautyshop value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);
            CharacterManager.Instance.EnterBeautyshop(player, genderTransfer: false);
        }
    }
}
