using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Sqllite;

public partial class ItemGradeEnchantingSupport
{
    public long? Id { get; set; }

    public long? ItemId { get; set; }

    public long? RequireGradeMax { get; set; }

    public long? AddSuccessRatio { get; set; }

    public long? AddSuccessMul { get; set; }

    public long? AddGreatSuccessRatio { get; set; }

    public long? AddGreatSuccessMul { get; set; }

    public long? AddBreakRatio { get; set; }

    public long? AddBreakMul { get; set; }

    public long? AddDowngradeRatio { get; set; }

    public long? AddDowngradeMul { get; set; }

    public long? AddGreatSuccessGrade { get; set; }

    public long? RequireGradeMin { get; set; }
}
