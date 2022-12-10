using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using AAEmu.Game.Models.Game.World.Transform;
using XmlH = AAEmu.Commons.Utils.XML.XmlHelper;

namespace AAEmu.Game.Models.Game.World.Xml
{
    public class XmlWorldZone
    {
        // <Zone name="w_gweonid_forest_1" id="129" originX="9" originY="14">
        public uint Id { get; set; }
        public string Name { get; set; }
        public int OriginX { get; set; }
        public int OriginY { get; set; }
        public ConcurrentDictionary<(int,int),XmlWorldCell> Cells { get; set; }
        public XmlWorld Parent { get; set; }
        public WorldSpawnPosition SpawnPosition { get; set; } = new WorldSpawnPosition(); // координаты для Zones

        public void ReadNode(XmlNode node, World world, XmlWorld xmlWorld)
        {
            Parent = xmlWorld;
            // Read XML
            var a = XmlH.ReadNodeAttributes(node);
            Id = XmlH.ReadAttribute<uint>(a, "id", 0u);
            Name = XmlH.ReadAttribute<string>(a, "name", "");
            OriginX = XmlH.ReadAttribute<int>(a, "originX", 0);
            OriginY = XmlH.ReadAttribute<int>(a, "originY", 0);
            var cellNodes = node.SelectNodes("cellList/cell");

            // Apply Data to world
            if (!world.ZoneKeys.Contains(Id))
                world.ZoneKeys.Add(Id);
            
            Cells = new ConcurrentDictionary<(int, int), XmlWorldCell>();
            if (cellNodes != null)
            {
                for (var i = 0; i < cellNodes.Count; i++)
                {
                    var cell = new XmlWorldCell();
                    cell.ReadNode(cellNodes[i], world, this);
                    if (!Cells.TryAdd((cell.X, cell.Y), cell))
                        throw new Exception($"Failed to add Cell {cell.X}, {cell.Y} in {Name}");
                }
            }
        }

        public override string ToString()
        {
            return $"{Id} - {Name}";
        }
    }
}
