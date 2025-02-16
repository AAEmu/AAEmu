using System;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Auction;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;

using NetCoreServer;

using NLog;

namespace AAEmu.Game.Services.WebApi.Controllers
{
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
            if (!jsonBody.TryGetProperty("itemId", out var itemIdElement) || 
                !jsonBody.TryGetProperty("quantity", out var quantityElement) || 
                !jsonBody.TryGetProperty("price", out var priceElement) || 
                !jsonBody.TryGetProperty("duration", out var durationElement) || 
                !jsonBody.TryGetProperty("clientId", out var clientIdElement) || 
                !jsonBody.TryGetProperty("clientName", out var clientNameElement))
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
                var newAuctionItem = AuctionManager.Instance.CreateAuctionLot(player, item, price, price, duration, 1, quantity);

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
                var auctionItems = AuctionManager.Instance.AuctionLots;
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
                var query = AuctionManager.Instance.AuctionLots.AsQueryable();

                // Extract query parameters from the URL
                var queryParams = HttpUtility.ParseQueryString(request.Url.Split('?').Length > 1 ? request.Url.Split('?')[1] : "");

                // Apply filters
                if (queryParams["itemId"] != null)
                {
                    var itemId = uint.Parse(queryParams["itemId"]);
                    query = query.Where(item => item.Item.TemplateId == itemId);
                }
                if (queryParams["clientName"] != null)
                {
                    var clientName = queryParams["clientName"];
                    query = query.Where(item => item.ClientName.Equals(clientName, StringComparison.OrdinalIgnoreCase));
                }
                if (queryParams["stackSize"] != null)
                {
                    var stackSize = uint.Parse(queryParams["stackSize"]);
                    query = query.Where(item => item.Item.Count == stackSize);
                }
                if (queryParams["directMoney"] != null)
                {
                    var directMoney = int.Parse(queryParams["directMoney"]);
                    query = query.Where(item => item.DirectMoney == directMoney);
                }
                if (queryParams["bidMoney"] != null)
                {
                    var bidMoney = int.Parse(queryParams["bidMoney"]);
                    query = query.Where(item => item.BidMoney == bidMoney);
                }
                if (queryParams["bidderName"] != null)
                {
                    var bidderName = queryParams["bidderName"];
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
    }
}
