using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Mails;

/// <summary>
/// The plot auction's prize letter. Bid refunds credit the bidder's account, online or not.
/// Sender keys and body shapes are the ones MailForAuction already ships; the subject is the
/// auction's own config name.
/// </summary>
public sealed class MailForPlotAuction : BaseMail
{
    /// <summary>Same key the item auction's lost-bid letter uses (client ships the locale entry).</summary>
    public const string RefundSender = ".auctionBidFail";

    /// <summary>Same key the item auction's won-bid letter uses.</summary>
    public const string PrizeSender = ".auctionBidWin";

    private MailForPlotAuction()
    {
        Header.SenderId = 0;
        Header.Status = MailStatus.Unread;
        var now = DateTime.UtcNow;
        Body.SendDate = now;
        Body.RecvDate = now;
    }

    /// <summary>Held bid money coming back: <c>body('&lt;auction name&gt;')</c>, one argument,
    /// exactly what the ".auctionBidFail" entry consumes.</summary>
    public static MailForPlotAuction ForBidRefund(uint receiverId, string receiverName, string auctionName, long copper)
    {
        var mail = new MailForPlotAuction
        {
            MailType = MailType.AucBidFail,
            ReceiverName = receiverName ?? string.Empty,
            Title = auctionName ?? string.Empty,
        };
        mail.Header.SenderName = RefundSender;
        mail.Header.ReceiverId = receiverId;
        mail.Body.Text = $"body('{auctionName}')";
        if (copper > 0)
            mail.AttachMoney(checked((int)copper));
        return mail;
    }

    /// <summary>The prize to a winner: <c>body('&lt;auction name&gt;', count, price)</c> — the
    /// three arguments ".auctionBidWin" consumes; price is what the winner's bid held.</summary>
    public static MailForPlotAuction ForPrize(
        uint receiverId, string receiverName, string auctionName, long price, IReadOnlyList<Item> prizes)
    {
        var mail = new MailForPlotAuction
        {
            MailType = MailType.AucBidWin,
            ReceiverName = receiverName ?? string.Empty,
            Title = auctionName ?? string.Empty,
        };
        mail.Header.SenderName = PrizeSender;
        mail.Header.ReceiverId = receiverId;

        var count = 0;
        foreach (var item in prizes)
        {
            item.OwnerId = receiverId;
            item.SlotType = SlotType.Mail;
            mail.Body.Attachments.Add(item);
            count += (int)Math.Min(int.MaxValue, item.Count);
        }

        mail.Body.Text = $"body('{auctionName}', {count}, {price})";
        return mail;
    }
}
