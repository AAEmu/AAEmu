using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id
{
    public class CharacterIdManager : IdManager
    {
        private static CharacterIdManager _instance;
        private const uint FirstId = 0x00000001;
        private const uint LastId = 0x00FFFFFF;
        private static readonly uint[] Exclude = { };
        private static readonly string[,] ObjTables = { { "characters", "id" } };

        public static CharacterIdManager Instance => _instance ?? (_instance = new CharacterIdManager());

        public CharacterIdManager() : base("CharacterIdManager", FirstId, LastId, ObjTables, Exclude)
        {
        }
    }
}