using System.Collections.Concurrent;
using AAEmu.Commons.Exceptions;
using AAEmu.Game.Models.CryEngine.Entities;

namespace AAEmu.Game.Models.CryEngine.Readers;

public class NetMissionReader : BaiReader
{
    public static int BaiTriangulationFileVersion = 55;

    public ConcurrentDictionary<long, NodeDescriptor> NodeDescriptorList { get; set; } = new();
    public List<LinkDescriptor> LinkDescriptorList { get; set; } = new();
    public Navigation Navigation { get; set; }
    public BBox BBox { get; set; }

    public NetMissionReader(System.IO.Stream rawStream, uint zoneId) : base(rawStream, zoneId)
    {
        Navigation = new Navigation(ZoneId);
        BBox = new BBox();
    }

    public override void CheckVersion(int version)
    {
        if (version > BaiTriangulationFileVersion)
        {
            throw new GameException("Wrong triangulation BAI file version " + version + " expected " + BaiTriangulationFileVersion);
        }
    }

    protected override void ReadFromFile()
    {
        if (Reader == null)
            return;

        BBox.Min = ReadVector3();
        BBox.Max = ReadVector3();

        var nodeCount = Reader.ReadInt32();
        for (var i = 0; i < nodeCount; i++)
        {
            var nodeDescriptor = new NodeDescriptor(this);
            nodeDescriptor.Id = Reader.ReadInt32();
            nodeDescriptor.Dir = ReadVector3(true);
            nodeDescriptor.Up = ReadVector3(true);
            nodeDescriptor.Pos = ReadVector3();
            nodeDescriptor.Index = Reader.ReadInt32();

            nodeDescriptor.Obstacle = new int[3];
            for (var j = 0; j < nodeDescriptor.Obstacle.Length; j++)
            {
                nodeDescriptor.Obstacle[j] = Reader.ReadInt32();
            }

            nodeDescriptor.Type = Reader.ReadByte();
            nodeDescriptor.Unk1 = Reader.ReadByte();
            nodeDescriptor.BitField0 = Reader.ReadByte();
            nodeDescriptor.Bitfield1 = Reader.ReadByte();
            if (!NodeDescriptorList.TryAdd(nodeDescriptor.Id, nodeDescriptor))
                Console.WriteLine($"Duplicate node ID {nodeDescriptor.Id}");
            //throw new Exception("Duplicate Id for NodeDescriptor");
            //NodeDescriptorList.TryAdd(nodeDescriptor.Id, nodeDescriptor);
        }

        var edgeCount = Reader.ReadInt32();
        for (var i = 0; i < edgeCount; i++)
        {
            var linkDescriptor = new LinkDescriptor(this);
            linkDescriptor.SourceNode = Reader.ReadUInt32();
            linkDescriptor.TargetNode = Reader.ReadUInt32();
            linkDescriptor.EdgeCenter = ReadVector3();
            linkDescriptor.MaxPassRadius = Reader.ReadSingle();
            linkDescriptor.Exposure = Reader.ReadSingle();
            linkDescriptor.Length = Reader.ReadSingle();
            linkDescriptor.MaxWaterDepth = Reader.ReadSingle();
            linkDescriptor.MinWaterDepth = Reader.ReadSingle();
            linkDescriptor.StartIndex = Reader.ReadByte();
            linkDescriptor.EndIndex = Reader.ReadByte();
            linkDescriptor.IsPureTriangularLink = (Reader.ReadByte() == 1);
            linkDescriptor.SimplePassabilityCheck = (Reader.ReadByte() == 1);
            // Cache source and target nodes
            linkDescriptor.SourceNodeDescriptor = this.NodeDescriptorList.GetValueOrDefault(linkDescriptor.SourceNode);
            linkDescriptor.TargetNodeDescriptor = this.NodeDescriptorList.GetValueOrDefault(linkDescriptor.TargetNode);
            LinkDescriptorList.Add(linkDescriptor);
        }
    }
}
