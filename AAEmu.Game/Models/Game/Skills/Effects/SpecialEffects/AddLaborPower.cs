using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class AddLaborPower : SpecialEffectAction
{
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
        if (caster is Character character)
        {
            // Non-zero trailing operands occur in content, but their native pool/recharge semantics
            // are not recovered yet. Do not silently apply those rows to a proven pool.
            if (value3 != 0 || value4 != 0)
            {
                Logger.Error(
                    "AddLaborPower rejected unsupported trailing operands value3 {0}, value4 {1}",
                    value3,
                    value4);
                return;
            }

            switch (value2)
            {
                case 0:
                    if (value1 is <= 0 or > short.MaxValue)
                    {
                        Logger.Error("AddLaborPower rejected invalid account-labor amount {0}", value1);
                        return;
                    }

                    character.ChangeLabor((short)value1, 0);
                    break;
                case 1:
                    character.AddLocalLaborPower(value1);
                    break;
                default:
                    Logger.Error("AddLaborPower rejected unknown pool selector {0} (amount {1})", value2, value1);
                    return;
            }

            Logger.Debug("Special effects: AddLaborPower value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);
        }
    }
}
