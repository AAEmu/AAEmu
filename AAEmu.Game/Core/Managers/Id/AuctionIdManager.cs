using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id;

public class AuctionIdManager() : IdManager("AuctionIdManager", FirstId, LastId, ObjTables, Exclude)
{
    private static AuctionIdManager _instance;
    private const uint FirstId = 0x00000001;
    private const uint LastId = 0xFFFFFFFF;
    private static readonly uint[] Exclude = [];
    private static readonly string[,] ObjTables = { { "auction_house", "id" } };

    public static AuctionIdManager Instance => _instance ?? (_instance = new AuctionIdManager());
}
