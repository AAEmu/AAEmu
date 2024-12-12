using System.Collections.Generic;
using System.IO;

namespace AAEmu.Game.Models.ClientData;

public class Hmap
{
    public byte Version { get; set; }
    public byte Dummy { get; set; }
    public byte Flags { get; set; }
    public byte Flags2 { get; set; }

    public int ChunkSize { get; set; }
    public int HeightMapSizeInUnits { get; set; }
    public int UnitSizeInMeters { get; set; }
    public int SectorSizeInMeters { get; set; }
    public int SectorsTableSizeInSectors { get; set; }
    public float HeightmapZRatio { get; set; }
    public float OceanWaterLevel { get; set; }

    public List<NodeCell> Nodes { get; set; } = [];

    public int Read(BinaryReader br, bool disabledReCalc)
    {
        Version = br.ReadByte();
        Dummy = br.ReadByte();
        Flags = br.ReadByte();
        Flags2 = br.ReadByte();

        // TODO: spawn endian, flags & 1 ? eBigEndian : eLittleEndian

        ChunkSize = br.ReadInt32();
        HeightMapSizeInUnits = br.ReadInt32();
        UnitSizeInMeters = br.ReadInt32();
        SectorSizeInMeters = br.ReadInt32();
        SectorsTableSizeInSectors = br.ReadInt32();
        HeightmapZRatio = br.ReadSingle();
        OceanWaterLevel = br.ReadSingle();

        if (Version >= 24)
            br.ReadBytes(128); // unk?

        var nodesRead = 0;
        while (br.BaseStream.Position != ChunkSize)
        {
            var node = new NodeCell();
            try
            {
                node.Read(br, disabledReCalc);
                nodesRead++;
            }
            catch
            {
                return -1;
            }

            Nodes.Add(node);
        }
        return nodesRead;
    }
}
