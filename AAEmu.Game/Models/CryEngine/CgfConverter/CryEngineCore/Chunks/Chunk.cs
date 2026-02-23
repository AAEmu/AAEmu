using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CgfConverter.Models;
using CgfConverter.Services;

namespace CgfConverter.CryEngineCore;

public abstract class Chunk : IBinaryChunk
{
    protected static Random rnd = new();
    protected static HashSet<int> alreadyPickedRandoms = new();

    private readonly static Dictionary<Type, Dictionary<uint, Func<Chunk>>> _chunkFactoryCache = new() { };

    internal ChunkHeader _header;
    internal Model _model;

    internal uint VersionRaw;
    internal bool IsBigEndian => (VersionRaw & 0x80000000u) != 0;
    internal uint Version => VersionRaw & 0x7fffffffu;
    internal int ID;
    internal uint Size;

    public uint Offset { get; internal set; }
    public ChunkType ChunkType { get; internal set; }
    public uint DataSize { get; set; }

    public static Chunk New(ChunkType chunkType, uint version)
    {
        return chunkType switch
        {
            ChunkType.SourceInfo => Chunk.New<ChunkSourceInfo>(version),
            ChunkType.Timing => Chunk.New<ChunkTimingFormat>(version),
            ChunkType.ExportFlags => Chunk.New<ChunkExportFlags>(version),
            ChunkType.MtlName => Chunk.New<ChunkMtlName>(version),
            ChunkType.DataStream => Chunk.New<ChunkDataStream>(version),
            ChunkType.Mesh => Chunk.New<ChunkMesh>(version),
            ChunkType.MeshSubsets => Chunk.New<ChunkMeshSubsets>(version),
            ChunkType.Node => Chunk.New<ChunkNode>(version),
            ChunkType.Helper => Chunk.New<ChunkHelper>(version),
            ChunkType.Controller => Chunk.New<ChunkController>(version),
            ChunkType.SceneProps => Chunk.New<ChunkSceneProp>(version),
            ChunkType.MeshPhysicsData => Chunk.New<ChunkMeshPhysicsData>(version),
            ChunkType.BoneAnim => Chunk.New<ChunkBoneAnim>(version),
            // Compiled chunks
            ChunkType.CompiledBones => Chunk.New<ChunkCompiledBones>(version),
            ChunkType.CompiledPhysicalProxies => Chunk.New<ChunkCompiledPhysicalProxies>(version),
            ChunkType.CompiledPhysicalBones => Chunk.New<ChunkCompiledPhysicalBones>(version),
            ChunkType.CompiledIntSkinVertices => Chunk.New<ChunkCompiledIntSkinVertices>(version),
            ChunkType.CompiledMorphTargets => Chunk.New<ChunkCompiledMorphTargets>(version),
            ChunkType.CompiledExt2IntMap => Chunk.New<ChunkCompiledExtToIntMap>(version),
            ChunkType.CompiledIntFaces => Chunk.New<ChunkCompiledIntFaces>(version),
            // Star Citizen equivalents
            ChunkType.CompiledBonesSC => Chunk.New<ChunkCompiledBones>(version),
            ChunkType.CompiledPhysicalBonesSC => Chunk.New<ChunkCompiledPhysicalBones>(version),
            ChunkType.CompiledExt2IntMapSC => Chunk.New<ChunkCompiledExtToIntMap>(version),
            ChunkType.CompiledIntFacesSC => Chunk.New<ChunkCompiledIntFaces>(version),
            ChunkType.CompiledIntSkinVerticesSC => Chunk.New<ChunkCompiledIntSkinVertices>(version),
            ChunkType.CompiledMorphTargetsSC => Chunk.New<ChunkCompiledMorphTargets>(version),
            ChunkType.CompiledPhysicalProxiesSC => Chunk.New<ChunkCompiledPhysicalProxies>(version),
            // SC IVO chunks
            ChunkType.MtlNameIvo => Chunk.New<ChunkMtlName>(version),
            ChunkType.MtlNameIvo320 => Chunk.New<ChunkMtlName>(version),
            ChunkType.CompiledBonesIvo => Chunk.New<ChunkCompiledBones>(version),
            ChunkType.CompiledBonesIvo320 => Chunk.New<ChunkCompiledBones>(version),
            ChunkType.MeshIvo => Chunk.New<ChunkMesh>(version),
            ChunkType.MeshIvo320 => Chunk.New<ChunkMesh>(version),
            ChunkType.IvoSkin2 => Chunk.New<ChunkIvoSkin>(version),
            ChunkType.IvoSkin => Chunk.New<ChunkIvoSkin>(version),
            // Old chunks
            ChunkType.BoneNameList => Chunk.New<ChunkBoneNameList>(version),
            ChunkType.MeshMorphTarget => Chunk.New<ChunkMeshMorphTargets>(version),
            ChunkType.BinaryXmlDataSC => Chunk.New<ChunkBinaryXmlData>(version),
            _ => new ChunkUnknown(),
        };
    }

    public static T New<T>(uint version) where T : Chunk
    {
        Func<Chunk> factory;

        lock (_chunkFactoryCache)
        {
            if (!_chunkFactoryCache.TryGetValue(typeof(T), out var versionMap))
            {
                versionMap = new Dictionary<uint, Func<Chunk>> { };
                _chunkFactoryCache[typeof(T)] = versionMap;
            }

            version &= 0x7fffffff;
            if (!versionMap.TryGetValue(version, out factory))
            {
                var targetType = typeof(T).Assembly.GetTypes()
                    .FirstOrDefault(type =>
                        !type.IsAbstract
                        && type.IsClass
                        && !type.IsGenericType
                        && typeof(T).IsAssignableFrom(type)
                        && type.Name == String.Format("{0}_{1:X}", typeof(T).Name, version));

                if (targetType != null)
                    factory = () => Activator.CreateInstance(targetType) as T;

                _chunkFactoryCache[typeof(T)][version] = factory;
            }
        }

        if (factory != null)
            return factory.Invoke() as T;

        throw new NotSupportedException($"Version {version:X} of {typeof(T).Name} is not supported");
    }

    public void Load(Model model, ChunkHeader header)
    {
        _model = model;
        _header = header;
    }

    public void SkipBytes(BinaryReader reader, long? bytesToSkip = null)
    {
        if (reader is null)
            return;

        /*
        if ((reader.BaseStream.Position > Offset + Size) && (Size > 0))
            Utilities.Log(LogLevelEnum.Debug, "Buffer Overflow in {2} 0x{0:X} ({1} bytes)", ID, reader.BaseStream.Position - Offset - Size, GetType().Name);

        if (reader.BaseStream.Length < Offset + Size)
            Utilities.Log(LogLevelEnum.Debug, "Corrupt Headers in {1} 0x{0:X}", ID, GetType().Name);
        */

        if (!bytesToSkip.HasValue)
            bytesToSkip = (long)(Size - Math.Max(reader.BaseStream.Position - Offset, 0));

        reader.BaseStream.Seek((long)(bytesToSkip), SeekOrigin.Current);
    }

    public virtual void Read(BinaryReader reader)
    {
        if (reader is null)
            throw new ArgumentNullException(nameof(reader));

        ChunkType = _header.ChunkType;
        VersionRaw = _header.VersionRaw;
        Offset = _header.Offset;
        ID = _header.ID;
        Size = _header.Size;
        DataSize = Size;          // For SC files, there is no header in chunks.  But need Datasize to calculate things.

        reader.BaseStream.Seek(_header.Offset, SeekOrigin.Begin);

        var swappableReader = (EndiannessChangeableBinaryReader)reader;

        // Star Citizen files don't have the type, version, offset and ID at the start of a chunk, so don't read them.
        if (_model.FileVersion == FileVersion.CryTek1And2 || _model.FileVersion == FileVersion.CryTek3)
        {
            // This part is always in little endian.
            swappableReader.IsBigEndian = false;

            ChunkType = (ChunkType)reader.ReadUInt32();
            if (!Enum.IsDefined(typeof(ChunkType), ChunkType))
            {
                reader.BaseStream.Seek(-4, SeekOrigin.Current);
                return;
            }

            VersionRaw = reader.ReadUInt32();
            Offset = reader.ReadUInt32();
            ID = reader.ReadInt32();
            DataSize = Size - 16;
        }

        if (ChunkType != _header.ChunkType ||
            VersionRaw != _header.VersionRaw ||
            Offset != _header.Offset ||
            ID != _header.ID)
        {
            //Utilities.Log(LogLevelEnum.Debug, "Conflict in chunk definition");
            //Utilities.Log(LogLevelEnum.Debug, "ChunkType=header({0:X}) vs header2({1:X})", (uint)_header.ChunkType, (uint)ChunkType);
            //Utilities.Log(LogLevelEnum.Debug, "VersionRaw=VersionRaw({0:X}) vs header2({1:X})", _header.VersionRaw, VersionRaw);
            //Utilities.Log(LogLevelEnum.Debug, "Offset=header({0:X}) vs header2({1:X})", _header.Offset, Offset);
            //Utilities.Log(LogLevelEnum.Debug, "ID=header({0:X}) vs header2({1:X})", _header.ID, ID);
            //Utilities.Log(LogLevelEnum.Debug, "ChunkType=header({0:X}) vs header2({1:X})", _header.ChunkType, ChunkType);
        }

        // Remainder of the chunk is in whichever endianness specified from version.
        swappableReader.IsBigEndian = IsBigEndian;
    }

    /// <summary>Gets a link to the SkinningInfo model.</summary>
    public SkinningInfo GetSkinningInfo()
    {
        if (_model.SkinningInfo is null)
            _model.SkinningInfo = new SkinningInfo();

        return _model.SkinningInfo;
    }

    public virtual void Write(BinaryWriter writer) { throw new NotImplementedException(); }

    public override string ToString() => $@"Chunk Type: {ChunkType}, Ver: {Version:X}, Offset: {Offset:X}, ID: {ID:X}, Size: {Size}";

    //public static IvoNodeId GetIvoNodeId(ChunkType chunkType)
    //{
    //    if (chunkType == ChunkType.Node)
    //        return IvoNodeId.NodeChunk;
    //    if (chunkType == ChunkType.Mesh)
    //        return IvoNodeId.MeshChunk;
    //    if (chunkType == ChunkType.MeshSubsets)
    //        return IvoNodeId.MeshSubsets;
    //    if (chunkType == ChunkType.Data)

    //}

    public static int GetNextRandom()
    {
        bool available = false;
        int rand = 0;
        while (!available) {
            rand = rnd.Next(100000);
            if (!alreadyPickedRandoms.Contains(rand))
            {
                alreadyPickedRandoms.Add(rand);
                available = true;
            }
        }
        return rand;
    }
}
