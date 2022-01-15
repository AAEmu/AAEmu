using System.Collections.Generic;
using System.Xml;
using XmlH = AAEmu.Commons.Utils.XML.XmlHelper;

namespace AAEmu.Game.Models.Game.World.Xml
{
    public class XmlWorldZoneListZone
    {
        // <Zone name="w_gweonid_forest_1" id="129" originX="9" originY="14">
        public uint Id { get; set; }
        public string Name { get; set; }
        public uint OriginX { get; set; }
        public uint OriginY { get; set; }
        public string CellListXml { get; set; }

        public void ReadNode(XmlNode node, World world, XmlWorldZoneList xmlZoneList)
        {
            // Read XML
            var a = XmlH.ReadNodeAttributes(node);
            Id = XmlH.ReadAttribute<uint>(a, "id", 0u);
            Name = XmlH.ReadAttribute<string>(a, "name", "");
            OriginX = XmlH.ReadAttribute<uint>(a, "originX", 0u);
            OriginY = XmlH.ReadAttribute<uint>(a, "originY", 0u);
            CellListXml = node.SelectSingleNode("cellList")?.OuterXml ?? "";

            // Apply Data to world
            
        }

        public override string ToString()
        {
            return $"{Id} - {Name}";
        }
    }
}
