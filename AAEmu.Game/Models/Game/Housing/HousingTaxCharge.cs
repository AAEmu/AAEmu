using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Models.Game.Housing;

/// <summary>
/// Records what a housing tax charge took (or will take) so a failed house write can refund
/// the same currency, and so wallet / seal rows can be persisted with the house.
/// </summary>
public sealed class HousingTaxCharge
{
    public static HousingTaxCharge Empty { get; } = new();

    /// <summary>Prestige already debited via expedition_members.</summary>
    public int ContributionPoints { get; set; }

    /// <summary>Bound tax certificates included in <see cref="SealPlans"/>.</summary>
    public int BoundCertificates { get; set; }

    /// <summary>Unbound tax certificates included in <see cref="SealPlans"/>.</summary>
    public int TaxCertificates { get; set; }

    /// <summary>Gold or AA points already subtracted from the character wallet.</summary>
    public int CopperAmount { get; set; }

    public bool UsedAaPoint { get; set; }

    public ItemTaskType TaskType { get; set; } = ItemTaskType.HouseDeposit;

    /// <summary>
    /// Planned seal consumptions. Not applied until the house (and these rows) commit together.
    /// </summary>
    public List<ItemConsumptionPlan> SealPlans { get; } = [];

    public bool RequiresCharacterSave => CopperAmount > 0;

    public bool HasPendingSealPlans => SealPlans.Count > 0;

    public bool IsEmpty =>
        ContributionPoints == 0 &&
        BoundCertificates == 0 &&
        TaxCertificates == 0 &&
        CopperAmount == 0 &&
        SealPlans.Count == 0;
}
