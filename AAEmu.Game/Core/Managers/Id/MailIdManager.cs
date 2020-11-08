using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id
{
    public class MailIdManager : IdManager
    {
        private static MailIdManager _instance;
        private const uint FirstId = 0x00002710; // 10000, no special reason
        private const uint LastId = 0xFFFFFFFF;
        private static readonly uint[] Exclude = { };
        private static readonly string[,] ObjTables = {{"mails", "id"}};

        public static MailIdManager Instance => _instance ?? (_instance = new MailIdManager());

        public MailIdManager() : base("MailIdManager", FirstId, LastId, ObjTables, Exclude)
        {
        }
    }
}
