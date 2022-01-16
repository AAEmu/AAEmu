using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Resolvers;
using XmlH = AAEmu.Commons.Utils.XML.XmlHelper;

namespace AAEmu.Game.Models.Game.World.Xml
{
    public class XmlWorldCell
    {
        // <cell x="9" y="14">
        public int X { get; set; }
        public int Y { get; set; }
        public XmlWorldZone Parent { get; set; }

        public void ReadNode(XmlNode node, World world, XmlWorldZone xmlWorldZone)
        {
            Parent = xmlWorldZone;
            
            // Read XML
            var a = XmlH.ReadNodeAttributes(node);
            X = XmlH.ReadAttribute<int>(a, "x", 0);
            Y = XmlH.ReadAttribute<int>(a, "y", 0);
            var sectorNodes = node.SelectNodes("sectorList/sector");

            // Apply Data to world
            if (sectorNodes != null)
            {
                for (var i = 0; i < sectorNodes.Count; i++)
                {
                    var sector = new XmlWorldSector();
                    sector.ReadNode(sectorNodes[i], world, this);
                }
            }
            
        }

        public override string ToString()
        {
            return $"{X},{Y}";
        }
    }
}
