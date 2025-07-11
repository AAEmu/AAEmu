using System.Numerics;
using AAEmu.Commons.Exceptions;

namespace AAEmu.Game.Models.CryEngine;

public class VertexMissionBai
{
    public int Version { get; set; }
    public int NodeCount { get; set; }
    public List<VertexMissionNode> Nodes { get; set; } = new();

    public void Parse(BinaryReader br)
    {
        Version = br.ReadInt32();
        NodeCount = br.ReadInt32();
        Nodes.Clear();
        for (var i = 0; i < NodeCount; i++)
        {
            var node = new VertexMissionNode();
            node.Parse(br);
            Nodes.Add(node);
        }
    }
}

public class VertexMissionNode
{
    public Vector3 Position { get; set; } = Vector3.Zero;
    public Vector3 Direction { get; set; } = Vector3.Zero;
    public float ApproxRadius { get; set; }
    public byte Flags { get; set; }
    public byte ApproxHeight { get; set; }

    public void Parse(BinaryReader br)
    {
        Position = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        Direction = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        ApproxRadius = br.ReadSingle();
        Flags = br.ReadByte();
        ApproxHeight = br.ReadByte();
        var unknown = br.ReadUInt16(); // ???
        if (unknown != 0)
            throw new GameException("Unknown wasn't zero, find out what this means !");
    }
}

