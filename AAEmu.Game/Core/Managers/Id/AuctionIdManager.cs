using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id;

public class AuctionIdManager : IdManager
{
    private static AuctionIdManager _instance;
    private const uint FirstId = 0x00000001;
    private const uint LastId = 0xFFFFFFFF;
    private static readonly uint[] Exclude = System.Array.Empty<uint>();
    private static readonly string[,] ObjTables = { { "auction_house", "id" } };

    public static AuctionIdManager Instance => _instance ?? (_instance = new AuctionIdManager());

    public AuctionIdManager() : base("AuctionIdManager", FirstId, LastId, ObjTables, Exclude)
    {
    }
}
