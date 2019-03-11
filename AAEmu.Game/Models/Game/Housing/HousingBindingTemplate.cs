using System.Collections.Generic;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Housing
{
    public class HousingBindingTemplate
    {
        public List<uint> TemplateId { get; set; }
        public Dictionary<uint, Point> AttachPointId { get; set; }
    }
}
