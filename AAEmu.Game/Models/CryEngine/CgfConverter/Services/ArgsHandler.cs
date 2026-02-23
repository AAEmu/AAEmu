using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CgfConverter.PackFileSystem;

namespace CgfConverter;

public sealed class ArgsHandler
{
    public readonly CascadedPackFileSystem PackFileSystem = new();
    public readonly List<Regex> ExcludeNodeNameRegexes = new();
    public readonly List<Regex> ExcludeMaterialNameRegexes = new();
    public readonly List<Regex> ExcludeShaderNameRegexes = new();
    
    public bool Verbose { get; set; }
    /// <summary>Files to process</summary>
    public List<string> InputFiles { get; internal set; }
    /// <summary>Location of the Object Files</summary>
    public List<string> DataDirs { get; internal set; } = new();
    /// <summary>File to render to</summary>
    public string? OutputFile { get; internal set; }
    /// <summary>Directory to render to</summary>
    public string? OutputDir { get; internal set; }
    /// <summary>Material file override</summary>
    public string? MaterialFile { get; internal set; }
    /// <summary>Whether to preserve path, if OutputDir is set.</summary>
    public bool PreservePath { get; internal set; }
    /// <summary>Maximum number of threads to use.</summary>
    public int MaxThreads { get; internal set; }
    /// <summary>Sets the output log level</summary>
    public LogLevelEnum LogLevel { get; set; } = LogLevelEnum.Critical;
    /// <summary>Allows naming conflicts for mtl file</summary>
    public bool AllowConflicts { get; internal set; }
    /// <summary>LODs files.  Adds _out onto the output</summary>
    public bool NoConflicts { get; internal set; }
    /// <summary>Name to group all meshes under</summary>
    public bool GroupMeshes { get; internal set; }
    /// <summary>Render Wavefront format files</summary>
    public bool OutputWavefront { get; internal set; }
    /// <summary>Render Collada format files</summary>
    public bool OutputCollada { get; internal set; }
    /// <summary>Render glTF</summary>
    public bool OutputGLTF { get; internal set; }
    /// <summary>Render glTF binary (default behavior)</summary>
    public bool OutputGLB { get; internal set; }
    /// <summary>Smooth Faces</summary>
    public bool Smooth { get; internal set; }
    /// <summary>Flag used to indicate we should convert texture paths to use TIFF instead of DDS</summary>
    public bool TiffTextures { get; internal set; }
    /// <summary>Flag used to indicate we should convert texture paths to use PNG instead of DDS</summary>
    public bool PngTextures { get; internal set; }
    /// <summary>Flag used to indicate we should convert texture paths to use TGA instead of DDS</summary>
    public bool TgaTextures { get; internal set; }
    /// <summary>Flag used to indicate that textures should not be included in the output file</summary>
    public bool NoTextures { get; internal set; }
    /// <summary>For glTF exports, embed textures into the glTF file instead of external references.</summary>
    public bool EmbedTextures { get; internal set; }
    /// <summary>Split each layer into different files, if a file does contain multiple layers.</summary>
    public bool SplitLayers { get; internal set; }
    /// <summary>List of node names to skip when rendering</summary>
    public List<string> ExcludeNodeNames { get; internal set; }
    /// <summary>List of material names to skipping the rendering of a mesh that uses the specified material</summary>
    public List<string> ExcludeMaterialNames { get; internal set; }
    /// <summary>List of shader names to skip when rendering</summary>
    public List<string> ExcludeShaderNames { get; internal set; }
    public bool Throw { get; internal set; }
    public bool DumpChunkInfo { get; internal set; }

    public ArgsHandler()
    {
        InputFiles = new List<string>();
        ExcludeNodeNames = new List<string>();
        ExcludeMaterialNames = new List<string>();
        ExcludeShaderNames = new List<string>();
    }

    /// <summary>
    /// Parse command line arguments
    /// </summary>
    /// <param name="inputArgs">list of arguments to parse</param>
    /// <returns>0 on success, 1 if anything went wrong</returns>
    public int ProcessArgs(string[] inputArgs)
    {
        var lookupDataDirs = new List<string>();
        var lookupInputs = new List<string>();
        
        for (int i = 0; i < inputArgs.Length; i++)
        {
            switch (inputArgs[i].ToLowerInvariant())
            {
                // Next item in list will be the Object directory
                case "-datadir":
                case "-objectdir":
                    if (++i > inputArgs.Length)
                    {
                        PrintUsage();
                        return 1;
                    }
                    
                    lookupDataDirs.Add(inputArgs[i]);
                    break;
                // Next item in list will be the output directory
                case "-out":
                case "-outdir":
                case "-outputdir":
                    if (++i > inputArgs.Length)
                    {
                        PrintUsage();
                        return 1;
                    }
                    OutputDir = new DirectoryInfo(inputArgs[i]).FullName;
                    break;
                case "-pp":
                case "-preservepath":
                    PreservePath = true;
                    break;
                case "-mt":
                case "-maxthreads":
                    if (++i > inputArgs.Length)
                    {
                        PrintUsage();
                        return 1;
                    }

                    if (int.TryParse(inputArgs[i], out var mt) && mt >= 0)
                    {
                        MaxThreads = mt;
                    }
                    else
                    {
                        Console.Error.WriteLine("Invalid number of threads {0}, defaulting to 1.", inputArgs[i]);
                        MaxThreads = 1;
                    }
                    break;
                case "-sl":
                case "-splitlayer":
                case "-splitlayers":
                    SplitLayers = true;
                    break;
                case "-loglevel":
                    if (++i > inputArgs.Length)
                    {
                        PrintUsage();
                        return 1;
                    }

                    if (Enum.TryParse(inputArgs[i], true, out LogLevelEnum level))
                        Utilities.LogLevel = level;
                    else
                    {
                        Console.Error.WriteLine("Invalid log level {0}, defaulting to warn", inputArgs[i]);
                        Utilities.LogLevel = LogLevelEnum.Warning;
                    }
                    break;
                case "-usage":
                    PrintUsage();
                    return 1;
                case "-smooth":
                    Smooth = true;
                    break;
                case "-obj":
                case "-object":
                case "-wavefront":
                    OutputWavefront = true;
                    break;
                case "-gltf":
                    OutputGLTF = true;
                    break;
                case "-glb":
                    OutputGLB = true;
                    break;
                case "-dae":
                case "-collada":
                    OutputCollada = true;
                    break;
                case "-tif":
                case "-tiff":
                    TiffTextures = true;
                    break;
                case "-png":
                    PngTextures = true;
                    break;
                case "-embedtextures":
                    EmbedTextures = true;
                    break;
                case "-tga":
                    TgaTextures = true;
                    break;
                case "-notex":
                    NoTextures = true;
                    break;
                case "-en":
                case "-excludenode":
                    if (++i > inputArgs.Length)
                    {
                        PrintUsage();
                        return 1;
                    }
                    ExcludeNodeNames.Add(inputArgs[i]);
                    break;
                case "-em":
                case "-excludemat":
                    if (++i > inputArgs.Length)
                    {
                        PrintUsage();
                        return 1;
                    }
                    ExcludeMaterialNames.Add(inputArgs[i]);
                    break;
                case "-es":
                case "-excludeshader":
                    if (++i > inputArgs.Length)
                    {
                        PrintUsage();
                        return 1;
                    }
                    ExcludeShaderNames.Add(inputArgs[i]);
                    break;
                case "-group":
                    GroupMeshes = true;
                    break;
                case "-throw":
                    Throw = true;
                    break;
                case "-mtl":
                case "-mat":
                case "-material":
                    if (++i > inputArgs.Length)
                    {
                        PrintUsage();
                        return 1;
                    }
                    MaterialFile = inputArgs[i];
                    break;
                case "-infile":
                case "-inputfile":
                    if (++i > inputArgs.Length)
                    {
                        PrintUsage();
                        return 1;
                    }
                    lookupInputs.Add(inputArgs[i]);
                    break;
                case "-allowconflicts":
                case "-allowconflict":
                    AllowConflicts = true;
                    break;
                case "-noconflict":
                case "-noconflicts":
                    NoConflicts = true;
                    break;
                case "-dump":
                case "-dumpchunk":
                case "-dumpchunkinfo":
                    DumpChunkInfo = true;
                    break;
                default:
                    lookupInputs.Add(inputArgs[i]);
                    break;
            }
        }

        // Ensure we have a file to process
        if (!lookupInputs.Any())
        {
            PrintUsage();
            return 1;
        }

        if (MaxThreads == 0)
            MaxThreads = Environment.ProcessorCount;
        Utilities.Log(LogLevelEnum.Info, $"Using up to {MaxThreads} threads");
        
        if (Smooth)
            Utilities.Log(LogLevelEnum.Info, "Smoothing Faces");
        if (GroupMeshes)
            Utilities.Log(LogLevelEnum.Info, "Grouping enabled");
        
        if (NoTextures)
            Utilities.Log(LogLevelEnum.Info, "Skipping texture output");
        else if (PngTextures)
            Utilities.Log(LogLevelEnum.Info, "Using PNG textures");
        else if (TiffTextures)
            Utilities.Log(LogLevelEnum.Info, "Using TIF textures");
        else if (TgaTextures)
            Utilities.Log(LogLevelEnum.Info, "Using TGA textures");
        if (MaterialFile is not null)
            Utilities.Log(LogLevelEnum.Info, $"Using material file: {MaterialFile}");

        if (OutputWavefront)
            Utilities.Log(LogLevelEnum.Info, "Output format set to Wavefront (.obj)");
        if (OutputCollada)
            Utilities.Log(LogLevelEnum.Info, "Output format set to COLLADA (.dae)");
        if (OutputGLTF)
            Utilities.Log(LogLevelEnum.Info, "Output format set to glTF (.gltf)");
        if (OutputGLB)
            Utilities.Log(LogLevelEnum.Info, "Output format set to glTF Binary (.glb)");

        if (AllowConflicts)
            Utilities.Log(LogLevelEnum.Info, "Allow conflicts for mtl files enabled");
        if (NoConflicts)
            Utilities.Log(LogLevelEnum.Info, "Prevent conflicts for mtl files enabled");
        if (ExcludeNodeNames.Any())
            Utilities.Log(LogLevelEnum.Info, $"Skipping nodes starting with any of these names: {String.Join(", ", ExcludeNodeNames)}");
        if (ExcludeMaterialNames.Any())
            Utilities.Log(LogLevelEnum.Info, $"Skipping meshes using materials named: {String.Join(", ", ExcludeMaterialNames)}");
        if (DumpChunkInfo)
            Utilities.Log(LogLevelEnum.Info, "Output chunk info for missing or invalid chunks.");
        if (Throw)
            Utilities.Log(LogLevelEnum.Info, "Exceptions thrown to debugger");

        if (OutputDir != null)
            Utilities.Log(LogLevelEnum.Info, "Output directory set to {0}", OutputDir);

        foreach (var dirAndOptions in lookupDataDirs)
        {
            var foundAny = false;

            var dirAndOptionStrings = dirAndOptions.Split("?", 2);
            var dir = dirAndOptionStrings[0].Trim();
            var packFileSystemOptions = dirAndOptionStrings.Length == 2
                ? dirAndOptionStrings[1].Split('&').Select(x => x.Split('=', 2)).ToDictionary(x => x[0].ToLowerInvariant(), x => x.Length == 2 ? x[1] : string.Empty)
                : new Dictionary<string, string>();
            
            if (Directory.Exists(dir))
            {
                Utilities.Log(LogLevelEnum.Info, "Source [Filesystem]: {0}", dir);
                DataDirs.Add(dir);
                PackFileSystem.Add(new RealFileSystem(dir));
                foundAny = true;
            }

            foreach (var globbed in PackFileSystem.Glob(dir))
            {
                if (globbed.EndsWith(WiiuStreamPackFileSystem.PackFileNameSuffix,
                        StringComparison.InvariantCultureIgnoreCase))
                {
                    Utilities.Log(LogLevelEnum.Info, "Source [Packfile]: {0}", globbed);
                    DataDirs.Add(globbed);
                    PackFileSystem.Add(new WiiuStreamPackFileSystem(PackFileSystem.GetStream(globbed), packFileSystemOptions));
                    foundAny = true;
                }
            }

            if (!foundAny)
                Utilities.Log(LogLevelEnum.Warning, "No corresponding source directory exist: {0}", dir);
        }

        foreach (var input in lookupInputs)
        {
            var foundAny = false;
            foreach (var globbed in PackFileSystem.Glob(input))
            {
                Utilities.Log(LogLevelEnum.Info, "Found input: {0}", globbed);
                InputFiles.Add(globbed);
                foundAny = true;
            }
            
            if (!foundAny)
                Utilities.Log(LogLevelEnum.Warning, "No corresponding input file exist: {0}", input);
        }
        
        ExcludeNodeNameRegexes.AddRange(ExcludeNodeNames.Select(x => new Regex(x, RegexOptions.Compiled | RegexOptions.IgnoreCase)));
        ExcludeMaterialNameRegexes.AddRange(ExcludeMaterialNames.Select(x => new Regex(x, RegexOptions.Compiled | RegexOptions.IgnoreCase)));
        ExcludeShaderNameRegexes.AddRange(ExcludeShaderNames.Select(x => new Regex(x, RegexOptions.Compiled | RegexOptions.IgnoreCase)));
        
        // Default to Collada format
        if (!OutputCollada && !OutputWavefront && !OutputGLB && !OutputGLTF)
            OutputCollada = true;

        return 0;
    }

    public static void PrintUsage()
    {
        Console.WriteLine();
        Console.WriteLine("cgf-converter [-usage] | <.cgf file> [-outputfile <output file>] [-dae] [-obj] [-glb] [-gltf] [-notex/-png/-tif/-tga] [-group] [-excludenode <nodename>] [-excludemat <matname>] [-loglevel <LogLevel>] [-throw] [-dump] [-objectdir <ObjectDir>]");
        Console.WriteLine();
        Console.WriteLine($"CryEngine Converter v{Assembly.GetExecutingAssembly().GetName().Version}");
        Console.WriteLine();
        Console.WriteLine("-usage:            Prints out the usage statement");
        Console.WriteLine();                        
        Console.WriteLine("<.cgf file>:       The name of the .cgf, .cga, .chr, .anim, .dba or .skin file to process.");
        Console.WriteLine("-outputfile:       (Optional) The name of the file to write the output.");
        Console.WriteLine("-objectdir:        (Optional but highly recommended) The name where the base Objects directory is located (i.e. where the .pak files were extracted).");
        Console.WriteLine("                   Defaults to current directory. Some packfile formats may accept additional options in the form of some.pack.file?key=value&key2=value2.");
        Console.WriteLine("-mtl/mat/material:  (Optional) The material file to use.");
        Console.WriteLine();                        
        Console.WriteLine(" Export formats.   By default -dae is used.");
        Console.WriteLine("-dae:              Export Collada format files."); 
        Console.WriteLine("-glb:              Export glb (glTF binary) files.");
        Console.WriteLine("-gltf:             Export file pairs of glTF and bin files."); 
        Console.WriteLine("-obj:              Export Wavefront format files (Not supported).");
        Console.WriteLine();                        
        Console.WriteLine("  Texture Options.   By default the converter will look for DDS files.");
        Console.WriteLine("-notex:            Do not include textures in outputs");
        Console.WriteLine("-tif:              Change the materials to look for .tif files instead of .dds.");
        Console.WriteLine("-png:              Change the materials to look for .png files instead of .dds.");
        Console.WriteLine("-tga:              Change the materials to look for .tga files instead of .dds.");
        Console.WriteLine("-embedtextures:    Embed textures into the glTF binary file instead of external references.");
        Console.WriteLine();                        
        Console.WriteLine("-smooth:           Smooth Faces.");
        Console.WriteLine("-group:            Group meshes into single model.");
        Console.WriteLine("-en/-excludenode < regular expression for node names>:");
        Console.WriteLine("                   Exclude matching nodes from rendering. Can be listed multiple times.");
        Console.WriteLine("-em/-excludemat <r egular expression for material names>:");
        Console.WriteLine("                   Exclude meshes with matching materials from rendering. Can be listed multiple times.");
        Console.WriteLine("-sm/-excludeshader  <material_name>:");
        Console.WriteLine("                   Exclude meshes with the material using matching shader from rendering. Can be listed multiple times.");
        Console.WriteLine("-noconflict:       Use non-conflicting naming scheme (<cgf File>_out.obj)");
        Console.WriteLine("-allowconflict:    Allows conflicts in .mtl file name. (obj exports only, as not an issue in dae.)");
        Console.WriteLine();                        
        Console.WriteLine("-prefixmatnames:   Prefixes material names with the filename of the source mtl file.");
        Console.WriteLine("-pp/-preservepath:");
        Console.WriteLine("                   Preserve the path hierarchy.");
        Console.WriteLine("-mt/-maxthreads <number>");
        Console.WriteLine("                   Set maximum number of threads to use. Specify 0 to use all cores.");
        Console.WriteLine("-sl/-splitlayer(s)");
        Console.WriteLine("                   Split into multiple layers (terrain only).");
        Console.WriteLine();
        Console.WriteLine("-loglevel:         Set the output log level (verbose, debug, info, warn, error, critical, none)");
        Console.WriteLine("-throw:            Throw Exceptions to installed debugger.");
        Console.WriteLine("-dump:             Dump missing/bad chunk info for support.");
        Console.WriteLine();
    }

    public override string ToString() => $@"Input file: {InputFiles}, Object Dir: {string.Join(',', DataDirs)}, Output file: {OutputFile}";
}
