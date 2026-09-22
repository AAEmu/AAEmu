using AAEmu.Game.GameData;

namespace AAEmu.Game.Models.Game.Mails;

/// <summary>
/// Mail charges and the normal-delivery delay, read from <c>content_configs</c>.
/// Every value is required: a missing row must fail the operation loudly instead of
/// silently charging a stale built-in number.
/// </summary>
public static class MailFeeRules
{
    public const string NormalMailCostKey = "normal_mail_cost";
    public const string ExpressMailCostKey = "express_mail_cost";
    public const string NormalAttachmentCostKey = "normal_mail_attachment_cost";
    public const string ExpressAttachmentCostKey = "express_mail_attachment_cost";
    public const string AttachmentDelayByTargetKey = "mail_attachment_delay_by_target";

    /// <summary>Postage for a normal (non-express) letter.</summary>
    public static int NormalMailCost => ContentConfigGameData.Instance.RequireInt(NormalMailCostKey);

    /// <summary>Postage for an express letter.</summary>
    public static int ExpressMailCost => ContentConfigGameData.Instance.RequireInt(ExpressMailCostKey);

    /// <summary>Charge per attachment slot on a normal letter.</summary>
    public static int NormalAttachmentCost => ContentConfigGameData.Instance.RequireInt(NormalAttachmentCostKey);

    /// <summary>Charge per attachment slot on an express letter.</summary>
    public static int ExpressAttachmentCost => ContentConfigGameData.Instance.RequireInt(ExpressAttachmentCostKey);

    /// <summary>
    /// How many attachment slots a letter carries before the per-attachment charge applies.
    /// The shipped content has no row for this count, so it stays a named constant; it is a
    /// threshold, not a price - all prices come from the rows above.
    /// </summary>
    public const int FreeAttachmentCount = 1;

    /// <summary>How long a normal letter takes to reach its recipient.</summary>
    public static TimeSpan NormalMailDelay =>
        TimeSpan.FromSeconds(ContentConfigGameData.Instance.RequireInt(AttachmentDelayByTargetKey));
}
