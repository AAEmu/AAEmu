using System;
using System.Collections.Generic;

namespace AAEmu.DB.Game
{
    public partial class PortalVisitedDistrict
    {
        public uint Id { get; set; }
        public uint Subzone { get; set; }
        public uint Owner { get; set; }
    }
}
