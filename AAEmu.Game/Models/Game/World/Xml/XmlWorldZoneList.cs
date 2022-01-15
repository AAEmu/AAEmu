using System.Collections.Generic;
using System.Xml;
using XmlH = AAEmu.Commons.Utils.XML.XmlHelper;

namespace AAEmu.Game.Models.Game.World.Xml
{
    public class XmlWorldZoneList
    {
        public List<XmlWorldZoneListZone> Zones { get; set; }

        public void ReadNode(XmlNode node, World world, XmlWorld xmlWorld)
        {
            Zones = new List<XmlWorldZoneListZone>();
            // Read XML
            var a = XmlH.ReadNodeAttributes(node);
            var zones = node.SelectNodes("Zone");
            for (var i = 0; i < zones.Count; i++)
            {
                var zone = new XmlWorldZoneListZone();
                zone.ReadNode(zones[i],world,this);
                Zones.Add(zone);
            }

            // Apply Data to world
        }
    }
}
