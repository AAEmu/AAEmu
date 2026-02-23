using CgfConverter.Models.Materials;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace CgfConverter.CryEngineCore;

public abstract class ChunkNode : Chunk          // cccc000b:   Node
{
    protected float VERTEX_SCALE = 1f / 100;

    public string Name { get; internal set; } = string.Empty;
    public int ObjectNodeID { get; internal set; }
    public int ParentNodeID { get; internal set; }  // Parent nodeID
    public int NumChildren { get; internal set; }
    public int MaterialID { get; internal set; }         // Chunk Id of the material for this node
    public Matrix4x4 Transform { get; internal set; }
    public Vector3 Pos { get; internal set; }       // Obsolete
    public Quaternion Rot { get; internal set; }    // Obsolete
    public Vector3 Scale { get; internal set; }     // Obsolete
    public int PosCtrlID { get; internal set; }
    public int RotCtrlID { get; internal set; }
    public int SclCtrlID { get; internal set; }
    public string Properties { get; internal set; } = string.Empty;
    public int PropertyStringLength { get; internal set; }

    /// <summary>Computed from material file. Not set for helper nodes, etc.</summary>
    public Material Materials { get; internal set; } = new();
    /// <summary>Name of the material library file.</summary>
    public string MaterialFileName { get; internal set; } = string.Empty;

    public Matrix4x4 LocalTransform => Matrix4x4.Transpose(Transform);

    private ChunkNode? _parentNode;

    public ChunkNode? ParentNode
    {
        get
        {
            if (ParentNodeID == ~0)  // aka 0xFFFFFFFF, or -1
                return null;

            if (_parentNode == null)
            {
                if (_model.ChunkMap.TryGetValue(ParentNodeID, out Chunk? node))
                    _parentNode = node as ChunkNode;
                else
                    _parentNode = _model.RootNode;
            }

            return _parentNode;
        }
        set
        {
            ParentNodeID = value == null ? ~0 : value.ID;
            _parentNode = value;
        }
    }

    private Chunk? _objectChunk;

    public Chunk? ObjectChunk
    {
        get
        {
            if (_objectChunk is null)
                _model.ChunkMap.TryGetValue(ObjectNodeID, out _objectChunk);

            return _objectChunk;
        }
    }

    public IEnumerable<ChunkNode> AllChildNodes => _model.NodeMap.Values.Where(a => a.ParentNodeID == ID);

    public override string ToString() => $@"Chunk Type: {ChunkType}, ID: {ID:X}, Version: {Version}, Name: {Name}, Object Node ID: {ObjectNodeID:X}, Parent Node ID: {ParentNodeID:X}, Mat: {MaterialID:X}";
}
