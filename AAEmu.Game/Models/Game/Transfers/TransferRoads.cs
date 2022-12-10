using System.Collections.Generic;

using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.Transfers
{
    public class TransferRoads
    {
        public string Name { get; set; }
        public uint ZoneId { get; set; }
        public int Type { get; set; } // TemplateId -> owner_id != 0, указывает на участок начала пути и на TemplateId транспорта для этого пути
        public int CellX { get; set; }
        public int CellY { get; set; }
        public List<WorldSpawnPosition> Pos { get; set; }

        public TransferRoads()
        {
            Pos = new List<WorldSpawnPosition>();
        }

    }
}
