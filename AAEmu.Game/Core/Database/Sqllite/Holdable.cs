using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Holdable
{
    public long? Id { get; set; }

    public long? KindId { get; set; }

    public long? Speed { get; set; }

    public long? ExtraDamagePierceFactor { get; set; }

    public long? ExtraDamageSlashFactor { get; set; }

    public long? ExtraDamageBluntFactor { get; set; }

    public long? MaxRange { get; set; }

    public long? Angle { get; set; }

    public long? AnimR1Id { get; set; }

    public long? AnimL1Id { get; set; }

    public long? PoseId { get; set; }

    public long? EnchantedDps1000 { get; set; }

    public long? SlotTypeId { get; set; }

    public long? AnimR2Id { get; set; }

    public long? AnimL2Id { get; set; }

    public long? AnimR3Id { get; set; }

    public long? AnimL3Id { get; set; }

    public long? AnimR1Ratio { get; set; }

    public long? AnimL1Ratio { get; set; }

    public long? AnimR2Ratio { get; set; }

    public long? AnimL2Ratio { get; set; }

    public string Name { get; set; }

    public string Code { get; set; }

    public long? DamageScale { get; set; }

    public long? SoundMaterialId { get; set; }

    public string FormulaDps { get; set; }

    public string FormulaMdps { get; set; }

    public string FormulaArmor { get; set; }

    public long? MinRange { get; set; }

    public string Comments { get; set; }

    public long? SheathePriority { get; set; }

    public double? DurabilityRatio { get; set; }

    public long? RenewCategory { get; set; }

    public long? ItemProcId { get; set; }

    public long? StatMultiplier { get; set; }

    public string FormulaHdps { get; set; }
}
