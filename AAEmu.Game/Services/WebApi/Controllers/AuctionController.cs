using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Auction;
using NetCoreServer;
using System.Text.Json;
using System.Text.RegularExpressions;
using NLog;
using System.Web;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Services.WebApi.Controllers;

/// AuctionController handles adding items to the auction house via a web API.
internal class AuctionController : BaseController, IController
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    /// Adds an item to the auction house.
    /// Returns A JSON response indicating success or failure.
    [WebApiPost("/api/auction/add")]
    public HttpResponse AddAuctionItem(HttpRequest request, MatchCollection matches)
    {
        // Deserialize the JSON body of the request
        var jsonBody = JsonSerializer.Deserialize<JsonElement>(request.Body);

        // Validate and extract required parameters
        if (!jsonBody.TryGetProperty("ItemId", out var itemIdElement) ||
            !jsonBody.TryGetProperty("Quantity", out var quantityElement) ||
            !jsonBody.TryGetProperty("Price", out var priceElement) ||
            !jsonBody.TryGetProperty("Duration", out var durationElement) ||
            !jsonBody.TryGetProperty("ClientId", out var clientIdElement) ||
            !jsonBody.TryGetProperty("ClientName", out var clientNameElement))
        {
            return BadRequestJson(new { error = "Invalid parameters" });
        }

        var itemId = itemIdElement.GetUInt64();
        var price = priceElement.GetInt64();
        var duration = (AuctionDuration)durationElement.GetInt32();
        var clientId = clientIdElement.GetUInt32();

        try
        {
            var player = WorldManager.Instance.GetCharacterById(clientId);
            var item = ItemManager.Instance.GetItemByItemId(itemId);
            if (player == null || item == null)
            {
                return BadRequestJson(new { error = "Internal server error", details = "Item not found!" });
            }

            if (!AuctionManager.Instance.PostLotOnAuction(player, item.Id, price, price, duration, 1, item.Count))
                return BadRequestJson(new { error = "Listing refused" });

            Logger.Info("Listed item {0} for {1} through the auction API", item.Id, player.Name);
            return OkJson(new { message = "Auction item added successfully" });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error adding auction item");
            return BadRequestJson(new { error = "Internal server error", details = ex.Message });
        }
    }

    /// Returns A JSON response with the list of all auction items.
    [WebApiGet("/api/auction/list")]
    public HttpResponse GetAllAuctionItems(HttpRequest request, MatchCollection matches)
    {
        try
        {
            var auctionItems = AuctionManager.Instance.AuctionLots.Values;
            return OkJson(new { items = auctionItems });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error retrieving auction items");
            return BadRequestJson(new { error = "Internal server error", details = ex.Message });
        }
    }

    /// Returns A JSON response with the filtered list of auction items.
    [WebApiGet("/api/auction/search")]
    public HttpResponse SearchAuctionItems(HttpRequest request, MatchCollection matches)
    {
        try
        {
            var query = AuctionManager.Instance.AuctionLots.Values.AsQueryable();

            // Extract query parameters from the URL
            var queryParams = HttpUtility.ParseQueryString(request.Url.Split('?').Length > 1 ? request.Url.Split('?')[1] : "");

            // Apply filters
            if (queryParams["ItemId"] != null)
            {
                var itemId = uint.Parse(queryParams["ItemId"]);
                query = query.Where(item => item.Item.TemplateId == itemId);
            }
            if (queryParams["ClientName"] != null)
            {
                var clientName = queryParams["ClientName"];
                query = query.Where(item => item.ClientName.Equals(clientName, StringComparison.OrdinalIgnoreCase));
            }
            if (queryParams["StackSize"] != null)
            {
                var stackSize = uint.Parse(queryParams["StackSize"]);
                query = query.Where(item => item.Item.Count == stackSize);
            }
            if (queryParams["DirectMoney"] != null)
            {
                var directMoney = long.Parse(queryParams["DirectMoney"]);
                query = query.Where(item => item.DirectMoney == directMoney);
            }
            if (queryParams["BidMoney"] != null)
            {
                var bidMoney = long.Parse(queryParams["BidMoney"]);
                query = query.Where(item => item.BidMoney == bidMoney);
            }
            if (queryParams["BidderName"] != null)
            {
                var bidderName = queryParams["BidderName"];
                query = query.Where(item => item.BidderName.Equals(bidderName, StringComparison.OrdinalIgnoreCase));
            }

            var auctionItems = query.ToList();
            return OkJson(new { items = auctionItems });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error retrieving auction items");
            return BadRequestJson(new { error = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Generates a new item and AH listing based on provided json body
    /// </summary>
    /// <param name="request"></param>
    /// <param name="matches"></param>
    /// <returns></returns>
    [WebApiPost("/api/auction/generate")]
    public HttpResponse GenerateAuctionItem(HttpRequest request, MatchCollection matches)
    {
        // Deserialize the JSON body of the request
        var jsonBody = JsonSerializer.Deserialize<JsonElement>(request.Body);

        // Validate and extract required parameters
        if (!jsonBody.TryGetProperty("ItemTemplateId", out var itemElement) ||
            !jsonBody.TryGetProperty("Quantity", out var quantityElement) ||
            !jsonBody.TryGetProperty("GradeId", out var gradeElement) ||
            !jsonBody.TryGetProperty("BuyNowPrice", out var buyNowPriceElement) ||
            !jsonBody.TryGetProperty("StartPrice", out var startPriceElement) ||
            !jsonBody.TryGetProperty("Duration", out var durationElement) ||
            !jsonBody.TryGetProperty("ClientId", out var clientIdElement) ||
            !jsonBody.TryGetProperty("ClientName", out var clientNameElement))
        {
            return BadRequestJson(new { error = "Invalid parameters" });
        }

        var itemTemplateId = itemElement.GetUInt32();
        var clientId = clientIdElement.GetUInt32();
        var clientName = clientNameElement.GetString();
        _ = quantityElement;
        _ = gradeElement;
        _ = buyNowPriceElement;
        _ = startPriceElement;
        _ = durationElement;

        Logger.Warn("Refused auction generate for template {0} by {1} ({2}): minting through this API is disabled",
            itemTemplateId, clientName, clientId);
        return BadRequestJson(new { error = "Auction generate is disabled. List an existing bag item through /api/auction/add." });
    }

}
