using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id;

public class SlaveIdManager : IdManager
{
    private static SlaveIdManager _instance;
    private const uint FirstId = 0x00000001;
    private const uint LastId = 0x00FFFFFF;
    private static readonly uint[] Exclude = System.Array.Empty<uint>();
    private static readonly string[,] ObjTables = { { "slaves", "id" } };

    public static SlaveIdManager Instance => _instance ?? (_instance = new SlaveIdManager());

    public SlaveIdManager() : base("SlaveIdManager", FirstId, LastId, ObjTables, Exclude)
    {
    }
}
