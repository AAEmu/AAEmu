namespace AAEmu.Game.Models.Game.Mails;

/// <summary>
/// Mail retention: read mail is kept a short time, unread mail a long one, and building-demolishment
/// notices are kept regardless of read state. Windows are per mail type.
/// Expiry: attachments in unread mail are returned to the sender, in read mail deleted.
/// The demolition window is configurable (World.DemolitionMailRetentionDays) because the English and
/// Korean rule sets disagree and the shipped AA-CN help row carries no numerals to arbitrate.
/// </summary>
public static class MailRetentionRules
{
    /// <summary>Kept this long after the mail was read (opened, or first attachment claimed).</summary>
    public static readonly TimeSpan ReadRetention = TimeSpan.FromDays(5);

    /// <summary>
    /// Building-demolishment notices are kept this long regardless of read state. The English
    /// target help row (ui_texts.id=4465) gives 740 days; the Korean rule set that supplies the
    /// other windows here implies 365. It is configurable because the two target rule sets
    /// disagree and the shipped AA-CN row carries no numerals to arbitrate. Configure in
    /// <c>AAEmu.Game/Configurations/World.json</c> under <c>World.DemolitionMailRetentionDays</c>.
    /// </summary>
    public static TimeSpan DemolitionRetention =>
        TimeSpan.FromDays(AppConfiguration.Instance.World.DemolitionMailRetentionDays);

    /// <summary>How long an unread mail survives before it is returned/expired.</summary>
    public static TimeSpan UnreadRetention(MailType type) => type switch
    {
        // Player Mail / Specialty Payment Mail
        MailType.Normal or MailType.Express or MailType.Registered or MailType.Spam
            or MailType.SysSellBackpack => TimeSpan.FromDays(30),

        // Hero Mail
        MailType.HeroCandidateAlarm or MailType.HeroElectionItem => TimeSpan.FromDays(25),

        // System Mail / Building Trade Mail (auction has no documented bucket; system default)
        _ => TimeSpan.FromDays(90),
    };

    /// <summary>Demolition notices ignore the read/unread distinction.</summary>
    public static bool IgnoresReadState(MailType type) =>
        type is MailType.Demolish or MailType.DemolishWithPenalty;

    /// <summary>
    /// Whether an expiring letter goes back to its sender rather than being deleted: unread mail
    /// does, and so does a player letter whose cash-on-delivery charge is still unpaid, read or
    /// not. Its receiver cannot take the goods before paying, so deleting it would destroy the
    /// sender's items and coin. The return clears the charge.
    /// </summary>
    public static bool ReturnsOnExpiry(BaseMail mail) =>
        mail.Header.Status != MailStatus.Read || HasUnpaidCharge(mail);

    /// <summary>A player letter still carrying a cash-on-delivery charge.</summary>
    public static bool HasUnpaidCharge(BaseMail mail) =>
        mail.Header.SenderId > 0 && mail.Body.BillingAmount > 0;
}
