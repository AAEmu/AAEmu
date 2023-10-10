using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id;

public class SkillTlIdManager : IdManager
{
    private static SkillTlIdManager _instance;
    private const uint FirstId = 0x00000001;
    private const uint LastId = 0x0000FFFE;
    private static readonly uint[] Exclude = System.Array.Empty<uint>();
    private static readonly string[,] ObjTables = { { } };

    public static SkillTlIdManager Instance => _instance ?? (_instance = new SkillTlIdManager());

    public SkillTlIdManager() : base("SkillTlIdManager", FirstId, LastId, ObjTables, Exclude)
    {
    }
}
