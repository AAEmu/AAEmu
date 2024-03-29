using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id;

public class WorldIdManager : IdManager
{
    private static WorldIdManager _instance;
    private const uint FirstId = 0x00000064;
    private const uint LastId = 0xFFFFFFFE;
    private static readonly uint[] Exclude = { };
    private static readonly string[,] ObjTables = { { } };

    public static WorldIdManager Instance => _instance ?? (_instance = new WorldIdManager());

    public WorldIdManager() : base("WorldIdManager", FirstId, LastId, ObjTables, Exclude)
    {
        }
}