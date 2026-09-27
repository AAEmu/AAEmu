namespace AAEmu.Game.Models.Game.CashShop;

/// <summary>
/// One row of the cash shop checkout ledger: wallet cash converted into AA points.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="AuditIcsSale"/> because a checkout has no shop item and no
/// SKU. The row is staged on the same transaction as the debit and the credit, so the ledger
/// can never show a checkout that was not charged, nor a charge with no ledger row.
/// </remarks>
public sealed class AuditIcsAaPointPurchase
{
    public long Id { get; set; }

    /// <summary>Account the cash was taken from.</summary>
    public uint AccountId { get; set; }

    /// <summary>Character the cash was taken from and the AA points were granted to.</summary>
    public uint CharacterId { get; set; }

    /// <summary>Time of the checkout (UTC).</summary>
    public DateTime PurchaseDate { get; set; }

    /// <summary>Wallet cash charged for the checkout.</summary>
    public long CashSpent { get; set; }

    /// <summary>AA points granted for that cash.</summary>
    public long AaPoints { get; set; }

    /// <summary>Ratio the grant was computed with, as published to the client.</summary>
    public uint ExchangeRatio { get; set; }
}
