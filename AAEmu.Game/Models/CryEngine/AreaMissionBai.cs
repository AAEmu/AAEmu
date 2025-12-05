using System.Numerics;
using System.Text;
using NLog;
using AAEmu.Commons.Exceptions;

namespace AAEmu.Game.Models.CryEngine;

public class AreaMissionBai(uint zoneKey)
{
    private static Logger Logger { get; set; } = LogManager.GetCurrentClassLogger();
    public uint ZoneKey { get; init; } = zoneKey;
    public uint Version { get; set; }
    public uint NodeCount { get; set; }
    public List<AreaMissionNode> Nodes { get; set; } = [];

    public void Parse(BinaryReader br)
    {
        Version = br.ReadUInt32();
        Nodes.Clear();

        var nodeGroup = 0;
        var nodeType = 0;
        while (br.BaseStream.Position < br.BaseStream.Length - 8)
        {
            nodeGroup++;
            var aNodeCount = br.ReadUInt32();
            // Logger.Debug($"Nodes({nodeGroup}), Count:{aNodeCount}, Zone: {ZoneKey}, Type: {nodeType}");
            // Areas
            for (var i = 0; i < aNodeCount; i++)
            {
                // Logger.Debug($"Reading Node {i}/{aNodeCount} of Group {nodeGroup}({Nodes.Count}) @{br.BaseStream.Position}");
                try
                {
                    var node = new AreaMissionNode();
                    if (nodeGroup == 2)
                        node.NodeType = AreaMissionType.AINavigationModifier;
                    else if (nodeType >= 3)
                        node.NodeType = AreaMissionType.AIPath;
                    else
                        node.NodeType = AreaMissionType.ForbiddenArea;

                    node.DebugStartPos = br.BaseStream.Position;
                    node.DebugNode = i;
                    node.DebugNodeGroup = nodeGroup;
                    if ((ZoneKey != 0) && (nodeGroup == 2))
                        node.ParseZone2(br);
                    else
                        node.Parse(br, nodeGroup == 2 ? 0x14 : Version);
                    node.DebugEndPos = br.BaseStream.Position;
                    Nodes.Add(node);
                }
                catch (Exception ex)
                {
                    Logger.Debug($"AreaMissionBai Pos:{br.BaseStream.Position}, Group {nodeGroup}, Index {i}, Exception: {ex}");
                    return;
                }
            }

            if (aNodeCount == 0)
                nodeType++;
        }

        NodeCount = (uint)Nodes.Count;
    }
}

public class AreaMissionNode
{
    private static Logger Logger { get; set; } = LogManager.GetCurrentClassLogger();
    public int AreaNameSize { get; set; }
    public string AreaName { get; set; } = string.Empty;
    public uint VerticesCount { get; set; }
    public AreaMissionType NodeType { get; set; }
    public uint NodeIndex { get; set; }
    public List<Vector3> Vtx { get; set; } = [];

    public long DebugStartPos { get; set; }
    public long DebugEndPos { get; set; }
    public int DebugNode { get; set; }
    public int DebugNodeGroup { get; set; }
    public byte[] DebugExtra1Bytes { get; set; }
    public byte[] DebugExtra2Bytes { get; set; }

    public void Parse(BinaryReader br, uint version = 0x15)
    {
        switch (version)
        {
            case 0x13:
                AreaNameSize = br.ReadInt32();
                AreaName = Encoding.Default.GetString(br.ReadBytes(AreaNameSize));

                DebugExtra1Bytes = br.ReadBytes(38);
                //_ = br.ReadBytes(38);
                break;
            case 0x14:
                AreaNameSize = br.ReadInt32();
                var buffer14 = br.ReadBytes(0x24);
                AreaName = Encoding.Default.GetString(buffer14, 0, AreaNameSize);

                DebugExtra1Bytes = br.ReadBytes(21);
                /*
                // Bounding box?
                _ = br.ReadSingle(); // X1
                _ = br.ReadSingle(); // Y1
                _ = br.ReadSingle(); // X2
                _ = br.ReadSingle(); // Y2
                // ???
                _ = br.ReadUInt32();
                // ???
                _ = br.ReadByte();
                */
                break;
            case 0x15:
                AreaNameSize = br.ReadInt32();
                AreaName = Encoding.Default.GetString(br.ReadBytes(AreaNameSize));
                break;
            default:
                throw new GameException($"Unknown Version for parsing AreaMissionNode 0x{version:X}");
        }

        VerticesCount = br.ReadUInt32();
        if (VerticesCount > 4096)
        {
            // Logger.Debug("Something is fishy here");
            // return;
        }

        if (VerticesCount <= 0)
        {
            Logger.Debug("No points ?");
            // return;
        }

        Vtx.Clear();
        for (var i = 0; i < VerticesCount; i++)
            Vtx.Add(new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()));

        if (NodeType == AreaMissionType.AIPath)
        {
            DebugExtra2Bytes = br.ReadBytes(16);
            /*
            _ = br.ReadInt32(); // 1
            _ = br.ReadInt32(); // -1
            _ = br.ReadSingle(); // 0.4
            _ = br.ReadInt32(); // 0 or 3 ?
            */
        }

    }

    public void ParseZone2(BinaryReader br)
    {
        AreaNameSize = br.ReadInt32();
        AreaName = Encoding.Default.GetString(br.ReadBytes(AreaNameSize));

        DebugExtra1Bytes = br.ReadBytes(38);

        VerticesCount = br.ReadUInt32();
        if (VerticesCount > 2048)
        {
            Logger.Debug("Something is fishy here");
            return;
        }

        Vtx.Clear();
        for (var i = 0; i < VerticesCount; i++)
            Vtx.Add(new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle()));

        if (NodeType == AreaMissionType.AIPath)
        {
            DebugExtra2Bytes = br.ReadBytes(16);
            /*
            _ = br.ReadInt32(); // 1
            _ = br.ReadInt32(); // -1
            _ = br.ReadSingle(); // 0.4
            _ = br.ReadInt32(); // 0 or 3 ?
            */
        }
    }
}
