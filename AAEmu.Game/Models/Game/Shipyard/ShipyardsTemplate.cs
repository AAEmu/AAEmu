using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.Shipyard
{
    public class ShipyardsTemplate
    {
        public uint Id { get; set; }
        public string Name { get; set; }
        public uint MainModelId { get; set; }
        public uint ItemId { get; set; }
        public int CeremonyAnimTime { get; set; }
        public float SpawnOffsetFront { get; set; }
        public float SpawnOffsetZ { get; set; }
        public int BuildRadius { get; set; }
        public int TaxDuration { get; set; }
        public uint OriginItemId { get; set; }
        public int TaxationId { get; set; }

        public Dictionary<int, ShipyardSteps> ShipyardSteps { get; set; }

        public ShipyardsTemplate()
        {
            ShipyardSteps = new Dictionary<int, ShipyardSteps>();
        }
    }
}
