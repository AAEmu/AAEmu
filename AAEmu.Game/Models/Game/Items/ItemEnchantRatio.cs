namespace AAEmu.Game.Models.Game.Items;

/// <summary>Outcome of a grade-enchant (regrade) attempt, as sent in SCGradeEnchantResultPacket.</summary>
public enum GradeEnchantResult
{
    Break = 0,
    Downgrade = 1,
    Fail = 2,
    Success = 3,
    GreatSuccess = 4,
    Disable = 5
}

/// <summary>
/// One grade row of <c>item_enchant_ratios</c>: the odds (in 1/10000) and cost inputs for
/// regrading an item that is currently AT this grade.
/// </summary>
public class ItemEnchantRatio
{
    public uint GroupId { get; set; }
    public byte Grade { get; set; }
    public int SuccessRatio { get; set; }
    public int GreatSuccessRatio { get; set; }
    public int BreakRatio { get; set; }
    public int DowngradeRatio { get; set; }
    public int DisableRatio { get; set; }

    /// <summary>Currency/multiplier input of the grade_enchant_cost formula.</summary>
    public int Cost { get; set; }

    /// <summary>Absolute downgrade target range (-1/-1 when downgrades are impossible).</summary>
    public int DowngradeMin { get; set; }
    public int DowngradeMax { get; set; }

    public uint CurrencyId { get; set; }

    /// <summary>No outcome is possible at this grade (e.g. top grade rows).</summary>
    public bool IsDeadEnd =>
        SuccessRatio <= 0 && GreatSuccessRatio <= 0 && BreakRatio <= 0 &&
        DowngradeRatio <= 0 && DisableRatio <= 0;
}
