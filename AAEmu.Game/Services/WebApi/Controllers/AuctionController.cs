using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Auction;
using NetCoreServer;
using System.Text.Json;
using System.Text.RegularExpressions;
using NLog;

namespace AAEmu.Game.Services.WebApi.Controllers
{
    /// AuctionController handles adding items to the auction house via a web API.
    internal class AuctionController : BaseController, IController
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        /// Adds an item to the auction house.
        /// "request" The HTTP request containing the item details.
        /// "matches" The regex matches from the route.
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
            var quantity = quantityElement.GetUInt32();
            var price = priceElement.GetInt32();
            var duration = durationElement.GetInt32();
            var clientId = clientIdElement.GetUInt32();
            var clientName = clientNameElement.GetString();

            try
            {
                // Create a new auction item
                var newAuctionItem = new AuctionItem
                {
                    ID = AuctionManager.Instance.GetNextId(),
                    Duration = (byte)duration,
                    ItemID = itemId,
                    ObjectID = 0,
                    Grade = 0,
                    Flags = 0,
                    StackSize = quantity,
                    DetailType = 0,
                    CreationTime = DateTime.UtcNow,
                    EndTime = DateTime.UtcNow.AddSeconds(duration),
                    LifespanMins = 0,
                    Type1 = 0,
                    WorldId = 0,
                    UnpackDateTIme = DateTime.UtcNow,
                    UnsecureDateTime = DateTime.UtcNow,
                    WorldId2 = 0,
                    ClientId = clientId,
                    ClientName = clientName,
                    StartMoney = 0,
                    DirectMoney = price,
                    BidWorldID = 0,
                    BidderId = 0,
                    BidderName = "",
                    BidMoney = 0,
                    Extra = 0,
                    IsDirty = true
                };

                // Add the auction item to the auction house
                AuctionManager.Instance.AddAuctionItem(newAuctionItem);
                Logger.Info($"Added auction item: {newAuctionItem}");
                return OkJson(new { message = "Auction item added successfully", item = newAuctionItem });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error adding auction item");
                return BadRequestJson(new { error = "Internal server error", details = ex.Message });
            }
        }
    }

    /// Represents a request to add an auction item.
    public class AuctionItemRequest
    {
        public uint ItemId { get; set; }
        public uint Quantity { get; set; }
        public int Price { get; set; }
        public int Duration { get; set; }
        public uint ClientId { get; set; }
        public string ClientName { get; set; }
    }
}

/// Example api JSON Payload to POST
///{
///    "itemId": 1234,
///    "quantity": 10,
///    "price": 5000,
///    "duration": 3600,
///    "clientId": 5678,
///    "clientName": "PlayerName"
///}
