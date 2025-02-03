using System;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Auction;
using NetCoreServer;
using System.Text.Json;
using System.Text.RegularExpressions;
using NLog;
using System.Web;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;

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

        var itemId = itemIdElement.GetUInt32();
        var quantity = quantityElement.GetInt32();
        var price = priceElement.GetInt32();
        var duration = (AuctionDuration)durationElement.GetInt32();
        var clientId = clientIdElement.GetUInt32();
        var clientName = clientNameElement.GetString();

        try
        {
            var player = WorldManager.Instance.GetCharacterById(clientId);
            var item = ItemManager.Instance.GetItemByItemId(itemId);
            if (player == null || item == null)
            {
                return BadRequestJson(new { error = "Internal server error", details = "Item not found!" });
            }
            // Create a new auction item
            var newAuctionItem = AuctionManager.Instance.CreateAuctionLot(player.Id, player.Name, item, price, price, duration, 1, quantity);

            // Add the auction item to the auction house
            AuctionManager.Instance.AddAuctionLot(newAuctionItem);
            Logger.Info($"Added auction item: {newAuctionItem}");
            return OkJson(new { message = "Auction item added successfully", item = newAuctionItem });
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
                uint itemId = uint.Parse(queryParams["ItemId"]);
                query = query.Where(item => item.Item.TemplateId == itemId);
            }
            if (queryParams["ClientName"] != null)
            {
                string clientName = queryParams["ClientName"];
                query = query.Where(item => item.ClientName.Equals(clientName, StringComparison.OrdinalIgnoreCase));
            }
            if (queryParams["StackSize"] != null)
            {
                uint stackSize = uint.Parse(queryParams["StackSize"]);
                query = query.Where(item => item.Item.Count == stackSize);
            }
            if (queryParams["DirectMoney"] != null)
            {
                int directMoney = int.Parse(queryParams["DirectMoney"]);
                query = query.Where(item => item.DirectMoney == directMoney);
            }
            if (queryParams["BidMoney"] != null)
            {
                int bidMoney = int.Parse(queryParams["BidMoney"]);
                query = query.Where(item => item.BidMoney == bidMoney);
            }
            if (queryParams["BidderName"] != null)
            {
                string bidderName = queryParams["BidderName"];
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
        var quantity = quantityElement.GetInt32();
        var gradeId = gradeElement.GetByte();
        var buyNowPrice = buyNowPriceElement.GetInt32();
        var startPrice = startPriceElement.GetInt32();
        var duration = (AuctionDuration)durationElement.GetInt32();
        var clientId = clientIdElement.GetUInt32();
        var clientName = clientNameElement.GetString();

        try
        {
            var playerAhContainer = ItemManager.Instance.GetItemContainerForCharacter(clientId, SlotType.Auction, null, 0);
            if (!playerAhContainer.AcquireDefaultItemEx(ItemTaskType.Invalid, itemTemplateId, quantity,
                    gradeId, out var newItems, out _, 0, -1))
            {
                var err = $"Unable to create new item {itemTemplateId} for character {clientName} ({clientId})";
                Logger.Warn(err);
                return BadRequestJson(err);
            }

            if (newItems.Count != 1)
            {
                Logger.Warn($"Generate auction item request generated more than one entry! ({newItems.Count})");
            }

            if (newItems.Count < 1)
            {
                var err = $"No item was generated with template {itemTemplateId} for character {clientName} ({clientId})";
                Logger.Warn(err);
                return BadRequestJson(err);
            }
            
            var newItem = newItems[0]; 
            
            var player = NameManager.Instance.GetCharacterName(clientId);
            var itemTemplate = ItemManager.Instance.GetItemTemplateFromItemId(itemTemplateId);
            if (player == null || itemTemplate == null)
            {
                return BadRequestJson(new { error = "Internal server error", details = "Item not found!" });
            }
            // Create a new auction item
            var newAuctionItem = AuctionManager.Instance.CreateAuctionLot(clientId, clientName, newItem, startPrice, buyNowPrice, duration, 1, quantity);

            // Add the auction item to the auction house
            AuctionManager.Instance.AddAuctionLot(newAuctionItem);
            Logger.Info($"Added auction item: {newAuctionItem}");
            return OkJson(new { message = "Auction item generated successfully", item = newAuctionItem });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error adding auction item");
            return BadRequestJson(new { error = "Internal server error", details = ex.Message });
        }
    }

}
