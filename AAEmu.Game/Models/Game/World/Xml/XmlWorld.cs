using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Xml;
using AAEmu.Game.Core.Managers.World;
using XmlH = AAEmu.Commons.Utils.XML.XmlHelper;

namespace AAEmu.Game.Models.Game.World.Xml
{
    public class XmlWorld
    {
        public string Name { get; set; }
        public uint Resolution { get; set; }
        public uint UnitSize { get; set; }
        public uint CellXCount { get; set; }
        public uint CellYCount { get; set; }
        public uint IsInstance { get; set; }
        public uint NextLayerId { get; set; }
        public uint NextSurfaceId { get; set; }
        public float OceanLevel { get; set; }
        public float MaxTerrainHeight { get; set; }
        public uint IsReleaseBranch { get; set; }
        
        /// <summary>
        /// Zones by zoneKey
        /// </summary>
        public ConcurrentDictionary<uint,XmlWorldZone> Zones { get; set; }

        public void ReadNode(XmlNode node, World world)
        {
            // Read XML
            var a = XmlH.ReadNodeAttributes(node);
            Name = XmlH.ReadAttribute<string>(a, "name", "");
            Resolution = XmlH.ReadAttribute<uint>(a, "resolution", 1024);
            UnitSize = XmlH.ReadAttribute<uint>(a, "unitSize", 2);
            CellXCount = XmlH.ReadAttribute<uint>(a, "cellXCount", 0);
            CellYCount = XmlH.ReadAttribute<uint>(a, "cellYCount", 0);
            IsInstance = XmlH.ReadAttribute<uint>(a, "isInstance", 0);
            NextLayerId = XmlH.ReadAttribute<uint>(a, "nextLayerId", 0);
            NextSurfaceId = XmlH.ReadAttribute<uint>(a, "nextSurfaceId", 0);
            OceanLevel = XmlH.ReadAttribute<float>(a, "oceanLevel", 100f);
            MaxTerrainHeight = XmlH.ReadAttribute<float>(a, "maxTerrainHeight", 4096f);
            IsReleaseBranch = XmlH.ReadAttribute<uint>(a, "isReleaseBranch", 0);

            // Apply Data to world
            world.Name = Name;
            world.CellX = (int)CellXCount;
            world.CellY = (int)CellYCount;
            world.OceanLevel = OceanLevel;
            world.MaxHeight = MaxTerrainHeight;

            // pre-create heightmap data
            world.HeightMaps = new ushort[world.CellX * WorldManager.CELL_HMAP_RESOLUTION, world.CellY * WorldManager.CELL_HMAP_RESOLUTION];
            // world.HeightMaps = new ushort[world.CellX * 512, world.CellY * 512];
            world.HeightMaxCoefficient = ushort.MaxValue / (world.MaxHeight / 4.0);
            
            // pre-create the required Sectors
            world.Regions = new Region[world.CellX * WorldManager.SECTORS_PER_CELL, world.CellY * WorldManager.SECTORS_PER_CELL];
            // Xml zone stuff cache
            world.XmlWorldZones = new ConcurrentDictionary<uint, XmlWorldZone>();

            var zoneNodes = node.SelectNodes("ZoneList/Zone");
            Zones = new ConcurrentDictionary<uint, XmlWorldZone>();
            
            // Read Zone XML
            if (zoneNodes != null)
            {
                for (var i = 0; i < zoneNodes.Count; i++)
                {
                    var zone = new XmlWorldZone();
                    zone.ReadNode(zoneNodes[i], world, this);
                    if (!Zones.TryAdd(zone.Id, zone))
                        throw new Exception("Duplicate zoneKey while reading world.xml");
                    world.XmlWorldZones.TryAdd(zone.Id, zone);
                }
            }

        }
    }
}
