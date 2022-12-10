using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Resolvers;
using AAEmu.Game.Core.Managers.World;
using XmlH = AAEmu.Commons.Utils.XML.XmlHelper;

namespace AAEmu.Game.Models.Game.World.Xml
{
    public class XmlWorldSector
    {
        // <sector x="0" y="2"/>
        public int X { get; set; }
        public int Y { get; set; }
        public XmlWorldCell Parent { get; set; }

        public void ReadNode(XmlNode node, World world, XmlWorldCell xmlWorldCell)
        {
            Parent = xmlWorldCell;
            
            // Read XML
            var a = XmlH.ReadNodeAttributes(node);
            X = XmlH.ReadAttribute<int>(a, "x", 0);
            Y = XmlH.ReadAttribute<int>(a, "y", 0);

            // Apply Data to world
            var worldSector = world.GetRegion(WorldSectorX(), WorldSectorY());
            worldSector.ZoneKey = Parent.Parent.Id;
        }

        private int WorldSectorX()
        {
            return X + (Parent.X * WorldManager.SECTORS_PER_CELL);
        }

        private int WorldSectorY()
        {
            return Y + (Parent.Y * WorldManager.SECTORS_PER_CELL);
        }
        
        public override string ToString()
        {
            return $"{X},{Y}";
        }
    }
}
