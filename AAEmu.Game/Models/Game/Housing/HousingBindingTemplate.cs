using System.Collections.Generic;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.Housing
{
    public class HousingBindingTemplate
    {
        public List<uint> TemplateId { get; set; }
        public Dictionary<sbyte, WorldSpawnPosition> AttachPointId { get; set; }
    }
}
