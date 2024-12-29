using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id;

public class GimmickIdManager : IdManager
{
    private static GimmickIdManager _instance;
    private const uint FirstId = 0x0001;
    private const uint LastId = 0xFFFE;
    private static readonly uint[] Exclude = [];
    private static readonly string[,] ObjTables = { { } };

    public static GimmickIdManager Instance => _instance ?? (_instance = new GimmickIdManager());

    public GimmickIdManager() : base("GimmickIdManager", FirstId, LastId, ObjTables, Exclude)
    {
    }
}
