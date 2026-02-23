using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using CgfConverter.Models;
using CgfConverter.Services;

namespace CgfConverter.CryEngineCore;

public class Model
{
    /// <summary> The Root of the loaded object </summary>
    public ChunkNode? RootNode { get; internal set; }

    /// <summary> Gets the root nodes. </summary>
    public IEnumerable<ChunkNode> RootNodes => NodeMap.Values.Where(x => x.ParentNodeID == ~0);

    /// <summary> Lookup Table for Chunks, indexed by ChunkID </summary>
    public Dictionary<int, Chunk> ChunkMap { get; internal set; } = new();

    /// <summary> The name of the currently processed file </summary>
    public string? FileName { get; internal set; }

    /// <summary> The File Signature - CryTek for 3.5 and lower. CrCh for 3.6 and higher. #ivo for some SC files. </summary>
    public string? FileSignature { get; internal set; }

    /// <summary> The type of file (geometry or animation) </summary>
    public FileType FileType { get; internal set; }

    public FileVersion FileVersion { get; internal set; }

    /// <summary> Position of the Chunk Header table </summary>
    public int ChunkTableOffset { get; internal set; }

    /// <summary>Contains all the information about bones and skinning them.  This a reference to the Cryengine object, since multiple Models can exist for a single object).</summary>
    public SkinningInfo SkinningInfo { get; set; } = new();

    /// <summary> The Bones in the model.  The CompiledBones chunk will have a unique RootBone. </summary>
    public ChunkCompiledBones? Bones { get; internal set; }

    public uint NumChunks { get; internal set; }

    public List<ChunkHeader> chunkHeaders = new();

    private Dictionary<int, ChunkNode> nodeMap { get; set; }

    /// <summary> All NodeChunks for this model in a dictionary by chunk Id. </summary>
    public Dictionary<int, ChunkNode> NodeMap
    {
        get
        {
            if (nodeMap == null)
            {
                nodeMap = new Dictionary<int, ChunkNode>() { };
                ChunkNode? rootNode = null;
                RootNode = rootNode = (rootNode ?? RootNode);  // Each model will have it's own rootnode.

                foreach (ChunkNode node in ChunkMap.Values.Where(c => c.ChunkType == ChunkType.Node).OrderBy(ob=>ob.ID))
                {
                    // Preserve existing parents
                    if (nodeMap.TryGetValue(node.ID, out ChunkNode knownNode))
                    {
                        ChunkNode parentNode = knownNode.ParentNode;

                        if (parentNode is not null)
                            parentNode = nodeMap[parentNode.ID];

                        node.ParentNode = parentNode;
                    }

                    nodeMap[node.ID] = node;
                }
            }
            return nodeMap;
        }
    }

    public bool HasBones => Bones != null;

    public bool HasGeometry
    {
        get
        {
            var types = ChunkMap.Select(n => n.Value.ChunkType);
            if (ChunkMap.Select(n => n.Value.ChunkType).Contains(ChunkType.Mesh) || ChunkMap.Select(n => n.Value.ChunkType).Contains(ChunkType.MeshIvo))
                return true;

            return false;
        }
    }

    /// <summary> Load the specified stream as a Model</summary>
    public static Model FromStream(string fileName, Stream stream, bool closeStream =false)
    {
        try
        {
            var buffer = new Model();
            buffer.Load(fileName, stream);
            return buffer;
        }
        finally
        {
            if (closeStream)
                stream.Close();
        }
    }

    public bool IsIvoFile => FileSignature?.Equals("#ivo") ?? false;

    private void Load(string fileName, Stream fs)
    {
        FileName = fileName;

        using EndiannessChangeableBinaryReader reader = new(fs);
        // Get the header.  This isn't essential for .cgam files, but we need this info to find the version and offset to the chunk table
        ReadFileHeader(reader);
        ReadChunkTable(reader);
        ReadChunks(reader);
    }

    private void ReadFileHeader(BinaryReader b)
    {
        b.BaseStream.Seek(0, SeekOrigin.Begin);
        FileSignature = b.ReadFString(4);

        if (FileSignature == "CrCh")           // file signature v3.6+
        {
            FileVersion = (FileVersion)b.ReadUInt32();    // 0x746
            NumChunks = b.ReadUInt32();       // number of Chunks in the chunk table
            ChunkTableOffset = b.ReadInt32(); // location of the chunk table

            return;
        }
        else if (FileSignature == "#ivo")
        {
            FileVersion = (FileVersion)b.ReadUInt32();  // 0x0900
            NumChunks = b.ReadUInt32();
            ChunkTableOffset = b.ReadInt32();
            CreateDummyRootNode();
            return;
        }

        b.BaseStream.Seek(0, SeekOrigin.Begin);
        FileSignature = b.ReadFString(8);

        if (FileSignature == "CryTek")         // file signature v3.5-
        {
            FileType = (FileType)b.ReadUInt32();
            FileVersion = (FileVersion)b.ReadUInt32();    // 0x744 0x745
            ChunkTableOffset = b.ReadInt32() + 4;
            NumChunks = b.ReadUInt32();       // number of Chunks in the chunk table

            return;
        }

        throw new NotSupportedException($"Unsupported FileS ignature {FileSignature}");
    }

    private void CreateDummyRootNode()
    {
        ChunkNode rootNode = new ChunkNode_823
        {
            Name = Path.GetFileNameWithoutExtension(FileName!),
            ObjectNodeID = 2,      // No node IDs in #ivo files.  The actual mesh is the only node in the m file.
            ParentNodeID = ~0,     // No parent
            NumChildren = 0,     // Single object
            MaterialID = 11,
            Transform = Matrix4x4.Identity,
            ChunkType = ChunkType.Node,
            ID = 1
        };
        RootNode = rootNode;
        rootNode._model = this;
        ChunkMap.Add(1, rootNode);
    }

    private void ReadChunkTable(BinaryReader b)
    {
        b.BaseStream.Seek(ChunkTableOffset, SeekOrigin.Begin);

        for (int i = 0; i < NumChunks; i++)
        {
            ChunkHeader header = Chunk.New<ChunkHeader>((uint)FileVersion);
            header.Read(b);

            chunkHeaders.Add(header);
        }

        // Set sizes for versions that don't have sizes
        for (int i = 0; i < NumChunks; i++)
        {
            if (FileVersion == FileVersion.CryTek1And2 &&  i < NumChunks - 2)
                chunkHeaders[i].Size = chunkHeaders[i + 1].Offset - chunkHeaders[i].Offset;
        }
    }

    private void ReadChunks(BinaryReader reader)
    {
        foreach (ChunkHeader chunkHeaderItem in chunkHeaders)
        {
            var chunk = Chunk.New(chunkHeaderItem.ChunkType, chunkHeaderItem.Version);
            ChunkMap[chunkHeaderItem.ID] = chunk;

            chunk.Load(this, chunkHeaderItem);
            chunk.Read(reader);
            // Ensure we read to end of structure
            chunk.SkipBytes(reader);

            // Assume first node read in Model[0] is root node.  This may be bad if they aren't in order!
            if (chunkHeaderItem.ChunkType == ChunkType.Node && RootNode == null)
                RootNode = chunk as ChunkNode;

            // Add Bones to the model.  We are assuming there is only one CompiledBones chunk per file.
            if (chunkHeaderItem.ChunkType == ChunkType.CompiledBones ||
                chunkHeaderItem.ChunkType == ChunkType.CompiledBonesSC ||
                chunkHeaderItem.ChunkType == ChunkType.CompiledBonesIvo ||
                chunkHeaderItem.ChunkType == ChunkType.CompiledBonesIvo320)
            {
                Bones = chunk as ChunkCompiledBones;
            }
        }
    }
}
