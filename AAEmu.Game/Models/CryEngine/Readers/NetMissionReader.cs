using System.Collections.Concurrent;
using AAEmu.Commons.Exceptions;
using AAEmu.Game.Models.CryEngine.Entities;

namespace AAEmu.Game.Models.CryEngine.Readers;

public class NetMissionReader : BaiReader
{
    public static int BaiTriangulationFileVersion = 55;

    public ConcurrentDictionary<long, NodeDescriptor> NodeDescriptorList { get; set; } = new();
    public List<LinkDescriptor> LinkDescriptorList { get; set; } = [];
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
            var nodeDescriptor = new NodeDescriptor(this)
            {
                Id = Reader.ReadInt32(), Dir = ReadVector3(true), Up = ReadVector3(true), Pos = ReadVector3(),
                Index = Reader.ReadInt32(),
                Obstacle = new int[3]
            };

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
            var linkDescriptor = new LinkDescriptor(this)
            {
                SourceNode = Reader.ReadUInt32(), TargetNode = Reader.ReadUInt32(), EdgeCenter = ReadVector3(),
                MaxPassRadius = Reader.ReadSingle(),
                Exposure = Reader.ReadSingle(),
                Length = Reader.ReadSingle(),
                MaxWaterDepth = Reader.ReadSingle(),
                MinWaterDepth = Reader.ReadSingle(),
                StartIndex = Reader.ReadByte(),
                EndIndex = Reader.ReadByte(),
                IsPureTriangularLink = Reader.ReadByte() == 1,
                SimplePassabilityCheck = Reader.ReadByte() == 1
            };
            // Cache source and target nodes
            linkDescriptor.SourceNodeDescriptor = this.NodeDescriptorList.GetValueOrDefault(linkDescriptor.SourceNode);
            linkDescriptor.TargetNodeDescriptor = this.NodeDescriptorList.GetValueOrDefault(linkDescriptor.TargetNode);
            LinkDescriptorList.Add(linkDescriptor);
        }
    }
}
