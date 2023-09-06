using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class Housing
{
    public long? Id { get; set; }

    public string Name { get; set; }

    public long? CategoryId { get; set; }

    public long? MainModelId { get; set; }

    public long? DoorModelId { get; set; }

    public long? StairModelId { get; set; }

    public byte[] AutoZ { get; set; }

    public byte[] GateExists { get; set; }

    public long? Hp { get; set; }

    public long? RepairCost { get; set; }

    public double? GardenRadius { get; set; }

    public string Family { get; set; }

    public long? TaxationId { get; set; }

    public long? GuardTowerSettingId { get; set; }

    public long? CinemaId { get; set; }

    public double? CinemaRadius { get; set; }

    public double? AutoZOffsetX { get; set; }

    public double? AutoZOffsetY { get; set; }

    public double? AutoZOffsetZ { get; set; }

    public double? Alley { get; set; }

    public double? ExtraHeightAbove { get; set; }

    public double? ExtraHeightBelow { get; set; }

    public long? DecoLimit { get; set; }

    public string Comments { get; set; }

    public long? AbsoluteDecoLimit { get; set; }

    public long? HousingDecoLimitId { get; set; }

    public byte[] IsSellable { get; set; }

    public byte[] HeavyTax { get; set; }

    public byte[] AlwaysPublic { get; set; }

    public long? DemolishRefundItemId { get; set; }
}
