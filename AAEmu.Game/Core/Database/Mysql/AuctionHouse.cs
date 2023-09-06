using System;
using System.Collections.Generic;

namespace AAEmu.Game.Core.Database.Mysql;

/// <summary>
/// Listed AH Items
/// </summary>
public partial class AuctionHouse
{
    public int Id { get; set; }

    public sbyte Duration { get; set; }

    public int ItemId { get; set; }

    public int ObjectId { get; set; }

    public bool Grade { get; set; }

    public bool Flags { get; set; }

    public int StackSize { get; set; }

    public bool DetailType { get; set; }

    public DateTime CreationTime { get; set; }

    public DateTime EndTime { get; set; }

    public int LifespanMins { get; set; }

    public int Type1 { get; set; }

    public sbyte WorldId { get; set; }

    public string UnsecureDateTime { get; set; }

    public string UnpackDateTime { get; set; }

    public sbyte WorldId2 { get; set; }

    public int ClientId { get; set; }

    public string ClientName { get; set; }

    public int StartMoney { get; set; }

    public int DirectMoney { get; set; }

    public bool BidWorldId { get; set; }

    public int BidderId { get; set; }

    public string BidderName { get; set; }

    public int BidMoney { get; set; }

    public int Extra { get; set; }
}
