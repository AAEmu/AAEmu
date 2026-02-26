using System.Collections.Concurrent;
using System.Numerics;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.IO;
using CgfConverter;
using CgfConverter.CryEngineCore;
using CgfConverter.PackFileSystem;
using Jitter2.Dynamics;
using Jitter2.LinearMath;
using NLog;
using Pfim;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Texture = CgfConverter.Models.Materials.Texture;

namespace AAEmu.Game.Models.CryEngine;

public static class CryEngineModelHelper
{
    private static Logger Logger { get; set; } = LogManager.GetCurrentClassLogger();

    // Model Data Cache
    public static ConcurrentDictionary<string, CgfConverter.CryEngine> CryEngineModels { get; } = [];
    public static ConcurrentDictionary<string, List<JTriangle>> CryEngineModelsTriangleListCache { get; } = [];
    public static ConcurrentBag<string> CryEngineModelsFailedPaths { get; } = [];
    // Tracks whether each cached model's triangles came from a physics proxy (true) or visual mesh fallback (false).
    private static readonly ConcurrentDictionary<string, bool> CryEngineModelsHasPhysicsProxy = new();
    // public static Dictionary<string, ImageTexture> CryEngineTextureCache { get; } = [];
    // private static object _textureCacheLock = new();

    /// <summary>
    /// Backward-compatible overload — does not expose whether physics proxy was used.
    /// </summary>
    public static List<JTriangle> MakeModel(string modelFileName, string materialFile)
        => MakeModel(modelFileName, materialFile, out _);

    /// <summary>
    /// Loads collision geometry for a CGF model.
    /// <paramref name="usedPhysicsProxy"/> is true when geometry came from ChunkCompiledPhysicalProxies
    /// (explicit simplified collision mesh) and false when the visual mesh fallback was used.
    /// Callers should only apply triangle-count filters for visual mesh fallbacks — physics proxy
    /// geometry represents intentional collision shapes regardless of triangle count.
    /// </summary>
    public static List<JTriangle> MakeModel(string modelFileName, string materialFile, out bool usedPhysicsProxy)
    {
        var key = modelFileName.Replace('\\', '/').ToLower();
        var inputFile = modelFileName.Replace('/', Path.DirectorySeparatorChar);

        // Stops failed models from trying to load multiple times.
        if (CryEngineModelsFailedPaths.Contains(key))
        {
            usedPhysicsProxy = false;
            return [];
        }

        // Return directly from triangleList cache if possible
        if (CryEngineModelsTriangleListCache.TryGetValue(key, out var triangleList))
        {
            usedPhysicsProxy = CryEngineModelsHasPhysicsProxy.GetValueOrDefault(key, false);
            return triangleList;
        }

        if (!CryEngineModels.TryGetValue(key, out var modelData))
        {
            if (!IO.ClientFileManager.FileExists(modelFileName))
            {
                Logger.Warn($"MakeModel: file not found in pak: '{modelFileName}'");
                CryEngineModelsFailedPaths.Add(key);
                usedPhysicsProxy = false;
                return [];
            }

            try
            {
                materialFile = Path.ChangeExtension(materialFile, ".mtl");
                modelData = new CgfConverter.CryEngine(
                    modelFileName,
                    // new CgfConverter.PackFileSystem.RealFileSystem(rootDir),
                    new AaGamePakFileSystem(),
                    null,
                    /* ClientFileManager.FileExists(materialFile) ? materialFile : */ null
                );

                modelData.ProcessCryengineFiles();
                CryEngineModels.TryAdd(key, modelData);
            }
            catch (Exception ex)
            {
                Logger.Warn($"MakeModel: exception loading '{modelFileName}': {ex.Message}");
                CryEngineModelsFailedPaths.Add(key);
                usedPhysicsProxy = false;
                return [];
            }
        }

        // Prefer physics proxy geometry (explicit collision mesh).
        // Fall back to visual mesh (LOD0 only) for structural objects that have no physics proxy.
        var physicsTriangles = CreateTriangleListFromPhysicsProxies(modelData);
        if (physicsTriangles.Count > 0)
        {
            triangleList = physicsTriangles;
            usedPhysicsProxy = true;
        }
        else
        {
            triangleList = CreateTriangleListFromCryEngineData(modelData, out _);
            usedPhysicsProxy = false;
        }

        if (triangleList.Count <= 0)
        {
            var modelCount = modelData.Models?.Count ?? 0;
            var nodeCount = modelData.NodeMap?.Count ?? 0;
            Logger.Debug($"Failed to create triangle list for {inputFile} (models={modelCount}, nodes={nodeCount})");
            CryEngineModelsFailedPaths.Add(key);
        }
        CryEngineModelsHasPhysicsProxy.TryAdd(key, usedPhysicsProxy);
        CryEngineModelsTriangleListCache.TryAdd(key, triangleList);
        return triangleList;
    }

    /// <summary>
    /// Extracts collision geometry from ChunkCompiledPhysicalProxies (physics proxy meshes).
    /// These are the simplified collision shapes used by the game client for player-world collision,
    /// and are more accurate for floor detection than the visual render mesh.
    /// Only processes the first model that has proxies — .cgf + .cgfm are loaded as separate
    /// models but share the same physics geometry, so processing both causes duplication.
    /// </summary>
    public static List<JTriangle> CreateTriangleListFromPhysicsProxies(CgfConverter.CryEngine data)
    {
        var triangleList = new List<JTriangle>();

        if ((data.Models?.Count ?? 0) <= 0)
            return triangleList;

        foreach (var model in data.Models)
        {
            // Find all ChunkCompiledPhysicalProxies in this model's chunks
            foreach (var chunk in model.ChunkMap.Values)
            {
                if (chunk is not ChunkCompiledPhysicalProxies proxyChunk)
                    continue;

                if (proxyChunk.PhysicalProxies == null)
                    continue;

                foreach (var proxy in proxyChunk.PhysicalProxies)
                {
                    if (proxy.Vertices == null || proxy.Indices == null)
                        continue;
                    if (proxy.NumVertices < 3 || proxy.NumIndices < 3)
                        continue;

                    // Build triangles from proxy vertex/index data
                    for (var i = 0; i + 2 < proxy.NumIndices; i += 3)
                    {
                        var i0 = proxy.Indices[i];
                        var i1 = proxy.Indices[i + 1];
                        var i2 = proxy.Indices[i + 2];

                        if (i0 >= proxy.NumVertices || i1 >= proxy.NumVertices || i2 >= proxy.NumVertices)
                            continue;

                        var v0 = proxy.Vertices[i0];
                        var v1 = proxy.Vertices[i1];
                        var v2 = proxy.Vertices[i2];

                        // Swap Y<->Z: CryEngine Z-up -> Y-up
                        triangleList.Add(new JTriangle(
                            new JVector(v0.X, v0.Z, v0.Y),
                            new JVector(v1.X, v1.Z, v1.Y),
                            new JVector(v2.X, v2.Z, v2.Y)));
                    }
                }
            }

            // Stop after the first model that has physics proxies.
            // Additional models (.cgfm) contain render mesh data, not separate collision.
            if (triangleList.Count > 0)
                break;
        }

        // When no proxy is found, log chunk types so we can diagnose whether
        // the proxy chunk exists but wasn't parsed, or simply doesn't exist.
        if (triangleList.Count == 0 && (data.Models?.Count ?? 0) > 0)
        {
            var chunkTypes = data.Models
                .SelectMany(m => m.ChunkMap.Values)
                .Select(c => c.ChunkType.ToString())
                .Distinct()
                .OrderBy(t => t);
            Logger.Debug($"[PhysProxy] No proxy found in '{data.InputFile}' — chunk types: [{string.Join(", ", chunkTypes)}]");
        }

        return triangleList;
    }

    /// <summary>
    /// Takes the data from a CryEngine model from CGFConverter and create populate a RigidBody in the PhysicsManager
    /// </summary>
    public static List<JTriangle> CreateTriangleListFromCryEngineData(CgfConverter.CryEngine data, out List<string> detectedTextures)
    {
        var currentVertexPosition = 0;
        var tempIndicesPosition = 0;
        var tempVertexPosition = 0;
        var currentIndicesPosition = 0;
        detectedTextures = [];

        if ((data.Models?.Count ?? 0) <= 0)
        {
            // Logger.Debug($"Model contains no models!");
            return [];
        }

        // NOTE: We don't load textures for the game server as it (currently) has no use

        var triangleList = new List<JTriangle>();

        // Only process nodes from the first model (the main .cgf).
        // Additional models (.cgfm) are LOD variants — processing them causes duplicate geometry.
        var primaryModel = (data.Models?.Count ?? 0) > 0 ? data.Models[0] : null;

        foreach (var node in data.NodeMap.Values)
        {
            // Skip nodes from secondary models (LOD .cgfm files)
            if (primaryModel != null && node._model != primaryModel)
                continue;

            if (node.ObjectChunk == null)
            {
                Logger.Warn($"Skipped node with missing Object {node.Name}");
                continue;
            }

            switch (node.ObjectChunk.ChunkType)
            {
                case ChunkType.Mesh:
                    // Render Meshes
                    if ((node.ParentNode != null) && (node.ParentNode.ChunkType != ChunkType.Node))
                    {
                        Logger.Debug($"Debug: Rendering {node.Name} to parent {node.ParentNode.Name}");
                    }

                    // Grab the mesh and process that.
                    WriteObjNode(triangleList, node);
                    break;

                case ChunkType.Helper:
                    // Ignore Helpers nodes
                    // TODO: Investigate if there's something we should do here
                    break;

                default:
                    // Warn us if we're skipping other nodes of interest
                    Logger.Debug($"Debug: Skipped a {node.ObjectChunk.ChunkType} chunk");
                    break;
            }
        }

        return triangleList;

        // Nested helper function
        void WriteObjNode(List<JTriangle> tl, ChunkNode chunkNode) // Pass a node to this to have it write to the Stream
        {
            // Get the Transform here. It's the node chunk Transform.m(41/42/42) divided by 100, added to the parent transform.
            // The transform of a child has to add the transforms of ALL the parents.

            if (chunkNode.ObjectChunk is not ChunkMesh tmpMesh)
                return;

            if (tmpMesh.MeshSubsetsData ==
                0) // This is probably wrong.  These may be parents with no geometry, but still have an offset
            {
                // GD.Print($"Debug: *******Found a Mesh chunk with no Submesh ID (ID: {tmpMesh.ID:X}, Name: {chunkNode.Name}).  Skipping...");
                // tmpMesh.WriteChunk();
                // Utils.Log(LogLevelEnum.Debug, "Node Chunk: {0}", chunkNode.Name);
                // transform = cgfData.GetTransform(chunkNode, transform);
                return;
            }

            if (tmpMesh.VerticesData == 0 &&
                tmpMesh.VertsUVsData ==
                0) // This is probably wrong.  These may be parents with no geometry, but still have an offset
            {
                // GD.Print($"Debug: *******Found a Mesh chunk with no Vertex info (ID: {tmpMesh.ID:X}, Name: {chunkNode.Name}).  Skipping...");
                //tmpMesh.WriteChunk();
                //Utils.Log(LogLevelEnum.Debug, "Node Chunk: {0}", chunkNode.Name);
                //transform = cgfData.GetTransform(chunkNode, transform);
                return;
            }

            // Going to assume that there is only one VerticesData datastream for now.  Need to watch for this.   
            // Some 801 types have vertices and not VertsUVs.
            var tmpMtlName = chunkNode._model.ChunkMap.GetValue(chunkNode.MaterialID, null) as ChunkMtlName;
            var tmpMeshSubsets =
                tmpMesh._model.ChunkMap.GetValue(tmpMesh.MeshSubsetsData,
                    null) as ChunkMeshSubsets; // Listed as Object ID for the Node
            var tmpIndices = tmpMesh._model.ChunkMap.GetValue(tmpMesh.IndicesData, null) as ChunkDataStream;
            var tmpVertices = tmpMesh._model.ChunkMap.GetValue(tmpMesh.VerticesData, null) as ChunkDataStream;
            var tmpNormals = tmpMesh._model.ChunkMap.GetValue(tmpMesh.NormalsData, null) as ChunkDataStream;
            var tmpUVs = tmpMesh._model.ChunkMap.GetValue(tmpMesh.UVsData, null) as ChunkDataStream;
            var tmpVertsUVs = tmpMesh._model.ChunkMap.GetValue(tmpMesh.VertsUVsData, null) as ChunkDataStream;
            var tmpVertMats = tmpMesh._model.ChunkMap.GetValue(tmpMesh.VertMatsData, null) as ChunkDataStream;

            // We only use 3 things in obj files:  vertices, normals and UVs.  No need to process the Tangents.

            int numChildren = chunkNode.NumChildren; // use in a for loop to print the mesh for each child

            var tempVertexPosition = currentVertexPosition;
            var tempIndicesPosition = currentIndicesPosition;
            var transformSoFar = GetNestedTransformations(chunkNode);

            foreach (var meshSubset in tmpMeshSubsets.MeshSubsets)
            {
                if (tmpMesh.VerticesData == 0)
                {
                    // Dymek's code.  Scales the object by the bounding box.
                    var multiplerVector = System.Numerics.Vector3.Abs((tmpMesh.MinBound - tmpMesh.MaxBound) / 2f);
                    if (multiplerVector.X < 1)
                    {
                        multiplerVector.X = 1;
                    }

                    if (multiplerVector.Y < 1)
                    {
                        multiplerVector.Y = 1;
                    }

                    if (multiplerVector.Z < 1)
                    {
                        multiplerVector.Z = 1;
                    }

                    var boundaryBoxCenter = (tmpMesh.MinBound + tmpMesh.MaxBound) / 2f;

                    // Probably using VertsUVs (3.7+).  Write those vertices out. Do UVs at same time.
                    for (int j = meshSubset.FirstVertex;
                         j < meshSubset.NumVertices + meshSubset.FirstVertex;
                         j++)
                    {
                        // Let's try this using this node chunk's rotation matrix, and the transform is the sum of all the transforms.
                        // Get the transform.
                        System.Numerics.Vector3 vertex =
                            (tmpVertsUVs.Vertices[j] * multiplerVector) + boundaryBoxCenter;

                        // Use matrix operations for the maximum performance
                        vertex = System.Numerics.Vector3.Transform(vertex, transformSoFar);

                        // verts.Add(new  Vector3(vertex.X, vertex.Z, vertex.Y));
                        // f.WriteLine("v {0:F7} {1:F7} {2:F7}", safe(vertex.X), safe(vertex.Y), safe(vertex.Z));
                    }

                    // f.WriteLine();

                    // textures

                    for (int j = meshSubset.FirstVertex;
                         j < meshSubset.NumVertices + meshSubset.FirstVertex;
                         j++)
                    {
                        // f.WriteLine("vt {0:F7} {1:F7} 0", safe(tmpVertsUVs.UVs[j].U), safe(1 - tmpVertsUVs.UVs[j].V));
                    }

                }
                else
                {
                    for (int j = meshSubset.FirstVertex; j < meshSubset.NumVertices + meshSubset.FirstVertex; j++)
                    {
                        if (tmpVertices != null)
                        {
                            // Rotate/translate the vertex
                            // Use matrix operations for the maximum performance
                            var vertex = System.Numerics.Vector3.Transform(tmpVertices.Vertices[j], transformSoFar);

                            // verts.Add(new  Vector3(vertex.X, vertex.Z, vertex.Y));
                            // f.WriteLine("v {0:F7} {1:F7} {2:F7}", safe(vertex.X), safe(vertex.Y), safe(vertex.Z));
                        }
                        else
                        {
                            Logger.Debug($"Debug: Error rendering vertices for {chunkNode.Name}");
                        }
                    }

                    // f.WriteLine();

                    // textures

                    for (var j = meshSubset.FirstVertex; j < meshSubset.NumVertices + meshSubset.FirstVertex; j++)
                    {
                        // f.WriteLine("vt {0:F7} {1:F7} 0", safe(tmpUVs.UVs[j].U), safe(1 - tmpUVs.UVs[j].V));
                    }

                }

                // f.WriteLine();

                // Normals (we're not using these yet)

                if (tmpMesh.NormalsData != 0)
                {
                    for (var j = meshSubset.FirstVertex; j < meshSubset.NumVertices + meshSubset.FirstVertex; j++)
                    {

                        //f.WriteLine("vn {0:F7} {1:F7} {2:F7}",
                        //	tmpNormals.Normals[j].X,
                        //	tmpNormals.Normals[j].Y,
                        //	tmpNormals.Normals[j].Z);
                    }
                }


                // f.WriteLine("g {0}", this.GroupOverride ?? chunkNode.Name);

                //if (this.Args.Smooth)
                //{
                //	f.WriteLine("s {0}", this.FaceIndex++);
                //}

                // Now write out the faces info based on the MtlName

                // Choose vertex source: separate Vertices data (legacy) or VertsUVs (3.7+).
                // VertsUVs vertices are bounding-box compressed and need decompression.
                if (tmpVertices == null && tmpVertsUVs == null)
                    continue;

                // Precompute VertsUVs bounding box decompression parameters
                var useVertsUVs = tmpVertices == null;
                var bbMultiplier = System.Numerics.Vector3.One;
                var bbCenter = System.Numerics.Vector3.Zero;
                if (useVertsUVs)
                {
                    bbMultiplier = System.Numerics.Vector3.Abs((tmpMesh.MinBound - tmpMesh.MaxBound) / 2f);
                    if (bbMultiplier.X < 1) bbMultiplier.X = 1;
                    if (bbMultiplier.Y < 1) bbMultiplier.Y = 1;
                    if (bbMultiplier.Z < 1) bbMultiplier.Z = 1;
                    bbCenter = (tmpMesh.MinBound + tmpMesh.MaxBound) / 2f;
                }

                var vertArray = useVertsUVs ? tmpVertsUVs.Vertices : tmpVertices.Vertices;
                if (vertArray == null)
                    continue;

                for (var j = meshSubset.FirstIndex; j + 2 < meshSubset.FirstIndex + meshSubset.NumIndices; j += 3)
                {
                    if (j + 2 >= tmpIndices.Indices.Length)
                        break;

                    var i1 = tmpIndices.Indices[j];
                    var i2 = tmpIndices.Indices[j + 1];
                    var i3 = tmpIndices.Indices[j + 2];

                    if (i1 >= vertArray.Length || i2 >= vertArray.Length || i3 >= vertArray.Length)
                        continue;

                    // Get vertices — VertsUVs needs bounding box decompression first
                    System.Numerics.Vector3 v1, v2, v3;
                    if (useVertsUVs)
                    {
                        v1 = System.Numerics.Vector3.Transform(vertArray[i1] * bbMultiplier + bbCenter, transformSoFar);
                        v2 = System.Numerics.Vector3.Transform(vertArray[i2] * bbMultiplier + bbCenter, transformSoFar);
                        v3 = System.Numerics.Vector3.Transform(vertArray[i3] * bbMultiplier + bbCenter, transformSoFar);
                    }
                    else
                    {
                        v1 = System.Numerics.Vector3.Transform(vertArray[i1], transformSoFar);
                        v2 = System.Numerics.Vector3.Transform(vertArray[i2], transformSoFar);
                        v3 = System.Numerics.Vector3.Transform(vertArray[i3], transformSoFar);
                    }

                    // Convert CryEngine Z-up → Y-up (swap Y and Z)
                    tl.Add(new JTriangle(new JVector(v1.X, v1.Z, v1.Y), new JVector(v2.X, v2.Z, v2.Y), new JVector(v3.X, v3.Z, v3.Y)));
                }

                tempVertexPosition +=
                    meshSubset.NumVertices; // add the number of vertices so future objects can start at the right place
                tempIndicesPosition += meshSubset.NumIndices; // Not really used...
            }

            // Extend the current vertex, uv and normal positions by the length of those arrays.
            currentVertexPosition = tempVertexPosition;
            currentIndicesPosition = tempIndicesPosition;

            // Generate something to display
            // f.Name = $"Model_ID_0x{tmpMesh.ID:X2}_Name_{chunkNode.Name}";

            // Use Surface Tool to create the mesh
            /*
            var tmpResMesh = new ArrayMesh();
            st.Commit(tmpResMesh);
            f.Mesh = tmpResMesh;
            f.Mesh.SurfaceSetMaterial(0, material3D);
            */
            // f.MaterialOverride = material3D;

            // f.MakeMeshFromVertex(verts, Mesh.PrimitiveType.Triangles, col);
            // LocalAddChild(f);
        }
    }

    private static Matrix4x4 GetNestedTransformations(ChunkNode node)
    {
        if (node.ParentNode != null)
        {
            return node.Transform * GetNestedTransformations(node.ParentNode);
        }
        else
        {
            // Root node: include its Transform so child nodes accumulate the full chain.
            // For single-node models the root transform is typically Identity (no effect).
            // For multi-node models, skipping the root transform would place child meshes
            // in wrong positions, causing brush collision geometry to be displaced.
            return node.Transform;
        }
    }
}
