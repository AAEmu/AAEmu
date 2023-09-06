using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemGrade
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? GradeOrder { get; set; }

    public double? VarHoldableDps { get; set; }

    public double? VarHoldableArmor { get; set; }

    public double? VarHoldableMagicDps { get; set; }

    public double? VarWearableArmor { get; set; }

    public double? VarWearableMagicResistance { get; set; }

    public string ColorArgb { get; set; }

    public string Comments { get; set; }

    public double? DurabilityValue { get; set; }

    public long? IconId { get; set; }

    public string ColorArgbSecond { get; set; }

    public long? UpgradeRatio { get; set; }

    public long? StatMultiplier { get; set; }

    public long? RefundMultiplier { get; set; }

    public long? GradeEnchantSuccessRatio { get; set; }

    public long? GradeEnchantGreatSuccessRatio { get; set; }

    public long? GradeEnchantBreakRatio { get; set; }

    public long? GradeEnchantDowngradeRatio { get; set; }

    public long? GradeEnchantCost { get; set; }

    public double? VarHoldableHealDps { get; set; }

    public long? GradeEnchantDowngradeMin { get; set; }

    public long? GradeEnchantDowngradeMax { get; set; }

    public long? CurrencyId { get; set; }
}
