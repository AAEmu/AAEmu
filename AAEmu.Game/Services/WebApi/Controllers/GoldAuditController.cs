using System.Collections.Concurrent;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Mails;
using NetCoreServer;
using NLog;

namespace AAEmu.Game.Services.WebApi.Controllers;

/// <summary>
/// Provides endpoints for tracking high-value gold transfers via mail and trades.
/// </summary>
internal class GoldAuditController : BaseController
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// In-memory log of completed trades with gold above threshold.
    /// Shared with TradeManager via <see cref="TradeGoldLog"/>.
    /// </summary>
    public static ConcurrentQueue<TradeGoldRecord> RecentTradeGoldLog { get; } = new();

    /// <summary>Max trade records to keep in memory.</summary>
    public const int MaxTradeRecords = 500;

    /// <summary>
    /// GET /api/gold/mails?minCopper=10000000
    /// Returns all current mails that carry gold >= minCopper (default 10 000 000 copper = 1000 gold).
    /// </summary>
    [WebApiGet("/api/gold/mails")]
    public HttpResponse GetHighValueMails(HttpRequest request)
    {
        var qs = ParseQueryString(request.Url);
        var minCopper = 10_000_000; // 1000 gold default
        if (int.TryParse(qs["minCopper"], out var mc) && mc > 0) minCopper = mc;

        var mails = MailManager.Instance._allPlayerMails;
        if (mails == null) return OkJson(Array.Empty<object>());

        var results = mails.Values
            .Where(m => m.Body?.CopperCoins >= minCopper)
            .OrderByDescending(m => m.Body?.CopperCoins ?? 0)
            .Take(500)
            .Select(m => new
            {
                mailId = m.Id,
                type = m.MailType.ToString(),
                senderId = m.Header?.SenderId ?? 0,
                senderName = m.Header?.SenderName ?? "?",
                receiverId = m.Header?.ReceiverId ?? 0,
                receiverName = m.ReceiverName ?? "?",
                copperCoins = m.Body?.CopperCoins ?? 0,
                goldAmount = (m.Body?.CopperCoins ?? 0) / 10000.0,
                title = m.Title ?? "",
                sendDate = m.Body?.SendDate.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                status = (int)(m.Header?.Status ?? 0),
                attachmentCount = m.Header?.Attachments ?? 0,
            })
            .ToArray();

        return OkJson(results);
    }

    /// <summary>
    /// GET /api/gold/trades
    /// Returns the last N high-value trade gold transfers from the in-memory log.
    /// </summary>
    [WebApiGet("/api/gold/trades")]
    public HttpResponse GetHighValueTrades(HttpRequest request)
    {
        var records = RecentTradeGoldLog.ToArray()
            .OrderByDescending(r => r.Timestamp)
            .Take(500)
            .Select(r => new
            {
                tradeId = r.TradeId,
                senderName = r.SenderName,
                senderId = r.SenderId,
                receiverName = r.ReceiverName,
                receiverId = r.ReceiverId,
                copperAmount = r.CopperAmount,
                goldAmount = r.CopperAmount / 10000.0,
                direction = r.Direction,
                timestamp = r.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"),
                ownerItems = r.OwnerItemCount,
                targetItems = r.TargetItemCount,
            })
            .ToArray();

        return OkJson(records);
    }

    /// <summary>
    /// Called from TradeManager.FinishTrade() to log a high-value trade.
    /// </summary>
    public static void LogTradeGold(TradeGoldRecord record)
    {
        RecentTradeGoldLog.Enqueue(record);
        // Trim queue if it exceeds max
        while (RecentTradeGoldLog.Count > MaxTradeRecords)
            RecentTradeGoldLog.TryDequeue(out _);
    }
}

/// <summary>
/// Record for a gold trade transfer.
/// </summary>
public class TradeGoldRecord
{
    public uint TradeId { get; set; }
    public string SenderName { get; set; } = "";
    public uint SenderId { get; set; }
    public string ReceiverName { get; set; } = "";
    public uint ReceiverId { get; set; }
    public int CopperAmount { get; set; }
    public string Direction { get; set; } = ""; // "owner→target" or "target→owner"
    public DateTime Timestamp { get; set; }
    public int OwnerItemCount { get; set; }
    public int TargetItemCount { get; set; }
}
