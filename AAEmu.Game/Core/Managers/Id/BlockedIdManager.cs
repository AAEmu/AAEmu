using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id
{
    public class BlockedIdManager : IdManager
    {
        private static BlockedIdManager _instance;
        private const uint FirstId = 0x00000001;
        private const uint LastId = 0x00FFFFFF;
        private static readonly uint[] Exclude = { };
        private static readonly string[,] ObjTables = { { "blocked", "id" } };

        public static BlockedIdManager Instance => _instance ?? (_instance = new BlockedIdManager());

        public BlockedIdManager() : base("BlockedIdManager", FirstId, LastId, ObjTables, Exclude)
        {
        }

    }
}
