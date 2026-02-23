using CgfConverter.CryEngineCore;
using CgfConverter.CryXmlB;
using CgfConverter.Models;
using CgfConverter.PackFileSystem;
using CgfConverter.Utils;
using Extensions;
using NLog;
using Material = CgfConverter.Models.Materials.Material;

namespace CgfConverter;

public partial class CryEngine
{
    protected Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private const string invalidExtensionErrorMessage = "Warning: Unsupported file extension - please use a cga, cgf, chr or skin file.";

    private static readonly HashSet<string> validExtensions = new()
    {
        ".cgf",
        ".cga",
        ".chr",
        ".skin",
        ".anim",
        ".soc",
        ".caf",
        ".dba"
    };

    protected readonly TaggedLogger Log;

    public string Name => Path.GetFileNameWithoutExtension(InputFile).ToLower();
    public List<Model> Models { get; internal set; } = new();
    public List<Model> Animations { get; internal set; } = new();
    public ChunkNode RootNode { get; internal set; }
    public ChunkCompiledBones Bones { get; internal set; }  // move to skinning info
    public SkinningInfo? SkinningInfo { get; set; }
    public string InputFile { get; internal set; }
    public IPackFileSystem PackFileSystem { get; internal set; }
    public List<string>? MaterialFiles { get; set; } = new();
    public string? ObjectDir { get; set; }

    /// <summary>Dictionary of Materials.  Key is the mtlName chunk Name property (stripped of path and
    /// extension info), or the material file if provided.</summary>
    public Dictionary<string, Material> Materials { get; internal set; } = new();

    public List<Chunk> Chunks {
        get {
            _chunks ??= Models.SelectMany(m => m.ChunkMap.Values).ToList();
            return _chunks;
        }
    }

    public Dictionary<string, ChunkNode> NodeMap  // Cannot use the Node name for the key.  Across a couple files, you may have multiple nodes with same name.
    {
        get {
            if (_nodeMap is null)
            {
                _nodeMap = new Dictionary<string, ChunkNode>(StringComparer.InvariantCultureIgnoreCase) { };

                ChunkNode? rootNode = null;

                // GD.Print("Mapping Nodes");

                foreach (Model model in Models)
                {
                    model.RootNode = rootNode = (rootNode ?? model.RootNode);

                    foreach (ChunkNode node in model.ChunkMap.Values.Where(c => c.ChunkType == ChunkType.Node).Select(c => c as ChunkNode))
                    {
                        // Preserve existing parents
                        if (_nodeMap.ContainsKey(node.Name))
                        {
                            ChunkNode parentNode = _nodeMap[node.Name].ParentNode;

                            if (parentNode is not null)
                                parentNode = _nodeMap[parentNode.Name];

                            node.ParentNode = parentNode;
                        }

                        _nodeMap[node.Name] = node;    // TODO:  fix this.  The node name can conflict. (example?)
                    }
                }
            }

            return _nodeMap;
        }
    }

    private List<Chunk>? _chunks;
    private Dictionary<string, ChunkNode>? _nodeMap;

    public CryEngine(string filename, IPackFileSystem packFileSystem, TaggedLogger? parentLogger = null, string? materialFiles = null, string? objectDir = null)
    {
        Log = new TaggedLogger(Path.GetFileName(filename), parentLogger);
        InputFile = filename;
        PackFileSystem = packFileSystem;
        MaterialFiles = materialFiles?.Split(',').ToList();
        ObjectDir = objectDir;
    }

    public void ProcessCryengineFiles()
    {
        var inputFiles = new List<string> { InputFile };

        if (!validExtensions.Contains(Path.GetExtension(InputFile).ToLowerInvariant()))
        {
            Log.D(invalidExtensionErrorMessage);
            throw new FileLoadException(invalidExtensionErrorMessage, InputFile);
        }

        AutoDetectMFile(InputFile, InputFile, inputFiles);

        foreach (var file in inputFiles)
        {
            // Each file (.cga and .cgam if applicable) will have its own RootNode.
            // This can cause problems.  .cga files with a .cgam files won't have geometry for the one root node.
            var model = Model.FromStream(file, PackFileSystem.GetStream(file), true);

            if (RootNode is null)
                RootNode = model.RootNode;  // This makes the assumption that we read the .cga file before the .cgam file.

            Bones = Bones ?? model.Bones;
            Models.Add(model);
        }

        SkinningInfo = ConsolidateSkinningInfo(Models);

        // Create materials from the first model. First model contains material file chunks if filenames aren't provided.
        CreateMaterials();

        try
        {
            var chrparamsStream = PackFileSystem.GetStream(Path.ChangeExtension(InputFile, ".chrparams"));
            if (chrparamsStream is null || chrparamsStream.Length <= 0)
                return;
            var chrparams = CryXmlSerializer.Deserialize<ChrParams.ChrParams>(chrparamsStream);
            var trackFilePath = chrparams.Animations?.FirstOrDefault(x => x.Name == "$TracksDatabase" || x.Name == "#filepath")?.Path;
            if (trackFilePath is null)
                throw new FileNotFoundException();
            if (Path.GetExtension(trackFilePath) != "dba")
                trackFilePath = Path.ChangeExtension(trackFilePath, "dba");
            // Log.D("Associated animation track database file found at {0}", trackFilePath);
            var trackFileStream = PackFileSystem.GetStream(trackFilePath);
            if (trackFileStream is null || trackFileStream.Length <= 0)
                return;
            Animations.Add(Model.FromStream(trackFilePath, trackFileStream, true));
        }
        catch (FileNotFoundException)
        {
            // pass
        }
    }

    private void AutoDetectMFile(string filename, string inputFile, List<string> inputFiles)
    {
        var mFile = Path.ChangeExtension(filename, $"{Path.GetExtension(inputFile)}m");
        if (PackFileSystem.Exists(mFile))
        {
            // Log.D("Found geometry file {0}", mFile);
            inputFiles.Add(mFile);
        }
    }

    private static SkinningInfo? ConsolidateSkinningInfo(List<Model> models)
    {
        if (!models.Any(model => model.ChunkMap.Values.Any(chunk => chunk is ChunkCompiledBones)))
            return null;

        SkinningInfo skin = new();

        foreach (Model model in models)
        {
            if (model.SkinningInfo.IntFaces is not null)
                skin.IntFaces = model.SkinningInfo.IntFaces;

            if (model.SkinningInfo.IntVertices is not null)
                skin.IntVertices = model.SkinningInfo.IntVertices;

            if (model.SkinningInfo.LookDirectionBlends is not null)
                skin.LookDirectionBlends = model.SkinningInfo.LookDirectionBlends;

            if (model.SkinningInfo.MorphTargets is not null)
                skin.MorphTargets = model.SkinningInfo.MorphTargets;

            if (model.SkinningInfo.PhysicalBoneMeshes is not null)
                skin.PhysicalBoneMeshes = model.SkinningInfo.PhysicalBoneMeshes;

            if (model.SkinningInfo.BoneEntities is not null)
                skin.BoneEntities = model.SkinningInfo.BoneEntities;

            if (model.SkinningInfo.BoneMapping is not null)
                skin.BoneMapping = model.SkinningInfo.BoneMapping;

            if (model.SkinningInfo.Collisions is not null)
                skin.Collisions = model.SkinningInfo.Collisions;

            if (model.SkinningInfo.CompiledBones is not null)
                skin.CompiledBones = model.SkinningInfo.CompiledBones;

            if (model.SkinningInfo.Ext2IntMap is not null)
                skin.Ext2IntMap = model.SkinningInfo.Ext2IntMap;
        }

        return skin;
    }

    private void CreateMaterials()
    {
        // if -mtl arg is used, try to find the material files, set to the full path, and create materials from it.
        if (MaterialFiles is not null)
        {
            foreach (var materialFile in MaterialFiles)
            {
                var key = Path.ChangeExtension(materialFile, string.Empty); // Path.GetFileNameWithoutExtension(materialFile));
                var fullyQualifiedMaterialFile = GetFullMaterialFilePath(materialFile);
                if (fullyQualifiedMaterialFile is not null)
                {
                    var materials = MaterialUtilities.FromStream(PackFileSystem.GetStream(fullyQualifiedMaterialFile), materialFile, true);
                    Materials.Add(key, materials);
                }
                else
                    Logger.Warn($"Unable to find provided material file {materialFile}.  Checking material library chunks for materials.");
            }
            AssignMaterialsToNodes();
            return;
        }

        // No material files provided.  Check the material library chunks for materials.
        var materialLibraryFiles = Models[0].ChunkMap.Values
            .OfType<ChunkMtlName>()
            .Where(x => x.MatType == MtlNameType.Library || x.MatType == MtlNameType.Basic || x.MatType == MtlNameType.Single)
            .Select(x => x.Name);

        /*
        Log.I("Found following potential material files.  If you are not specifying a material file and the materials don't look right, trying one of the following files:");
        foreach (var file in materialLibraryFiles)
            Log.I($"   {file}");
        */

        MaterialFiles = GetMaterialFilesFromMatLibraryChunks(materialLibraryFiles)?.ToList();

        if (MaterialFiles is not null)
        {
            foreach (var materialFile in MaterialFiles)
            {
                var key = Path.GetFileNameWithoutExtension(materialFile);
                var fullyQualifiedMaterialFile = GetFullMaterialFilePath(materialFile);
                if (fullyQualifiedMaterialFile is not null)
                {
                    var materials = MaterialUtilities.FromStream(PackFileSystem.GetStream(fullyQualifiedMaterialFile), materialFile, true);
                    Materials.Add(key, MaterialUtilities.FromStream(PackFileSystem.GetStream(fullyQualifiedMaterialFile), materialFile, true));
                }
            }
        }

        // Unable to find any materials.  Create default mats for each library file.
        if (Materials.Count == 0)
        {
            var meshSubsets = Models.Last().ChunkMap.Values.OfType<ChunkMeshSubsets>()
                .SelectMany(c => c.MeshSubsets);

            var maxChildren = Models
                .SelectMany(x => x.ChunkMap.Values.OfType<ChunkMtlName>())
                .Select(x => x.NumChildren)
                .Max();

            var maxMats = (uint)meshSubsets.Select(x => x.MatID).Max() + 1;
            // set maxMats to the max of maxMats and maxChildren
            maxMats = Math.Max(maxMats, maxChildren);

            foreach (var materialFile in materialLibraryFiles)
            {
                var key = Path.GetFileNameWithoutExtension(materialFile);

                if (!Materials.ContainsKey(key))
                {
                    MaterialFiles.Add(materialFile);
                    var materials = CreateDefaultMaterials(maxMats);
                    Materials.Add(key, materials);
                }
            }
        }
        AssignMaterialsToNodes(false);
    }

    private void AssignMaterialsToNodes(bool mtlFilesProvided = true)
    {
        if (mtlFilesProvided)
        {
            foreach (var node in NodeMap.Values.Where(x => x.MaterialID != 0))
            {
                if (MaterialFiles.Count == 1)
                {
                    node.MaterialFileName = Path.GetFileNameWithoutExtension(MaterialFiles[0]);
                    node.Materials = Materials.Values.First();
                    continue;
                }

                var mtlNameChunk = Chunks.OfType<ChunkMtlName>().Where(x => x.ID == node.MaterialID).FirstOrDefault();
                var mtlNameKey = Path.GetFileNameWithoutExtension(mtlNameChunk?.Name) ?? "default";

                if (Materials.ContainsKey(mtlNameKey))
                {
                    node.MaterialFileName = mtlNameKey;
                    node.Materials = Materials[mtlNameKey];
                }
                else
                {
                    node.MaterialFileName = Materials.FirstOrDefault().Key;
                    node.Materials = Materials.FirstOrDefault().Value;
                }
            }
        }
        else
        {
            foreach (var node in NodeMap.Values.Where(x => x.MaterialID != 0))
            {
                var mtlNameChunk = Chunks.OfType<ChunkMtlName>().Where(x => x.ID == node.MaterialID).FirstOrDefault();
                var mtlNameKey = Path.GetFileNameWithoutExtension(mtlNameChunk?.Name) ?? "default";

                if (Materials.ContainsKey(mtlNameKey))
                {
                    node.MaterialFileName = mtlNameKey;
                    node.Materials = Materials[mtlNameKey];
                }
                else
                {
                    node.MaterialFileName = Materials.FirstOrDefault().Key;
                    node.Materials = Materials.FirstOrDefault().Value;
                }
            }
        }
    }

    private string? GetFullMaterialFilePath(string materialFile)
    {
        if (!materialFile.EndsWith(".mtl"))
            materialFile += ".mtl";

        // check if file exists as provided
        if (PackFileSystem.Exists(materialFile))
            return materialFile;

        // Check if it's in the same directory as the model file
        var modelPath = Path.GetDirectoryName(InputFile);
        if (modelPath is not null && PackFileSystem.Exists(FileHandlingExtensions.CombineAndNormalizePath(modelPath, materialFile)))
            return FileHandlingExtensions.CombineAndNormalizePath(modelPath, materialFile);

        // check if relative to objectdir if provided
        if (ObjectDir is not null && PackFileSystem.Exists(FileHandlingExtensions.CombineAndNormalizePath(ObjectDir, materialFile)))
            return FileHandlingExtensions.CombineAndNormalizePath(ObjectDir, materialFile);

        // unable to find material file.
        return null;
    }

    private Material CreateDefaultMaterials(uint maxNumberOfMaterials)
    {
        var materials = new Material
        {
            SubMaterials = new Material[maxNumberOfMaterials],
            Name = Name
        };

        for (var i = 0; i < maxNumberOfMaterials; i++)
            materials.SubMaterials[i] = MaterialUtilities.CreateDefaultMaterial($"material{i}", $"{i / (float)maxNumberOfMaterials},0.5,0.5");

        return materials;
    }

    // Gets the Library material file if one can be found. File will be the name in the library as it's the dictionary key.
    private HashSet<string>? GetMaterialFilesFromMatLibraryChunks(IEnumerable<string> libraryFileNames)
    {
        HashSet<string> materialFiles = new();
        if (libraryFileNames is not null)
        {
            foreach (var libraryFile in libraryFileNames)
            {
                var testFile = libraryFile;
                if (!testFile.EndsWith(".mtl"))
                    testFile += ".mtl";

                if (PackFileSystem.Exists(testFile))
                {
                    materialFiles.Add(libraryFile);
                    continue;
                }

                // Check if testFile + object dir exists
                var fullObjectDirPath = FileHandlingExtensions.CombineAndNormalizePath(ObjectDir, testFile);
                if (PackFileSystem.Exists(fullObjectDirPath))
                {
                    materialFiles.Add(libraryFile);
                    continue;
                }

                // Check in fully qualified current dir
                var fullPath = FileHandlingExtensions.CombineAndNormalizePath(Path.GetDirectoryName(InputFile), $"{libraryFile}.mtl");
                if (PackFileSystem.Exists(fullPath))
                {
                    materialFiles.Add(libraryFile);
                    continue;
                }
            }
            return materialFiles;
        }

        Log.W("Unable to find material file for {0}", InputFile);
        return null;
    }

    public static bool SupportsFile(string name) => validExtensions.Contains(Path.GetExtension(name).ToLowerInvariant());
}
