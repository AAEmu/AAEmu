using Jitter2.LinearMath;

namespace AAEmu.Game.Models.CryEngine;

public class NetMissionBai
{
    public int Version { get; set; }
    public JVector Minimum { get; set; } = JVector.Zero;
    public JVector Maximum { get; set; } = JVector.Zero;
    public int NodeCount { get; set; }
    public List<NetMissionNode> Nodes { get; set; } = [];
    public int EdgesCount { get; set; }
    public List<NetMissionEdge> Edges { get; set; } = [];
    public Dictionary<uint, NetMissionNode> NodeMap { get; set; } = new();

    public void Parse(BinaryReader br)
    {
        Version = br.ReadInt32();

        Minimum = new JVector(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        Maximum = new JVector(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

        NodeCount = br.ReadInt32();
        Nodes.Clear();
        for (var i = 0; i < NodeCount; i++)
        {
            var node = new NetMissionNode();
            node.Parse(br);
            Nodes.Add(node);
            NodeMap.Add(node.Id, node);
        }

        EdgesCount = br.ReadInt32();
        Edges.Clear();
        for (var i = 0; i < EdgesCount; i++)
        {
            var edge = new NetMissionEdge();
            edge.Parse(br);
            Edges.Add(edge);
        }
    }
}

public enum NetMissionNodeNavType : ushort
{
    EWayPointNodeType0 = 0,
    EWayPointNodeType1 = 1,
    EWayPointNodeType2 = 2,
    EWayPointNodeType3 = 3,
    Forbidden = 4,
    ForbiddenDesigner = 5,
    Removable = 6, // removable?
}

public class NetMissionNode
{
    public uint Id { get; set; }
    // public byte[] Unknown24; // 24 bytes

    public JVector Direction { get; set; } = JVector.Zero;
    public JVector Up { get; set; } = JVector.Zero;
    public JVector Position { get; set; } = JVector.Zero;

    public int Index { get; set; }
    public int Obstacles0 { get; set; }
    public int Obstacles1 { get; set; }
    public int Obstacles2 { get; set; }

    public NetMissionNodeNavType NavType { get; set; }

    public byte Bitfield0 { get; set; }
    public byte Bitfield1 { get; set; }

    public void Parse(BinaryReader br)
    {
        Id = br.ReadUInt32();
        Direction = new JVector(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        Up = new JVector(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        Position = new JVector(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        Index = br.ReadInt32();
        Obstacles0 = br.ReadInt32();
        Obstacles1 = br.ReadInt32();
        Obstacles2 = br.ReadInt32();
        NavType = (NetMissionNodeNavType)br.ReadUInt16();
        Bitfield0 = br.ReadByte();
        Bitfield1 = br.ReadByte();
    }
}

public class NetMissionEdge
{
    public uint FromId { get; set; }
    public uint ToId { get; set; }
    public JVector Position { get; set; } = JVector.Zero;
    public float MaxPassRadius { get; set; }
    public float Exposure { get; set; }
    public float Length { get; set; }
    public float MaxWaterDepth { get; set; }
    public float MinWaterDepth { get; set; }
    public byte StartIndex { get; set; }
    public byte EndIndex { get; set; }
    public byte IsPureTriangularLink { get; set; }
    public byte SimplePassAbilityCheck { get; set; }

    public void Parse(BinaryReader br)
    {
        FromId = br.ReadUInt32();
        ToId = br.ReadUInt32();
        Position = new JVector(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        MaxPassRadius = br.ReadSingle();
        Exposure = br.ReadSingle();
        Length = br.ReadSingle();
        MaxWaterDepth = br.ReadSingle();
        MinWaterDepth = br.ReadSingle();
        StartIndex = br.ReadByte();
        EndIndex = br.ReadByte();
        IsPureTriangularLink = br.ReadByte();
        SimplePassAbilityCheck = br.ReadByte();
    }
}
