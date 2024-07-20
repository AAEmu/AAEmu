using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AAEmu.Game.Models;
using NLog;

namespace AAEmu.Game.IO;

public static class ClientFileManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private static bool _initialized = false;

    private static List<ClientSource> sources = new();

    public static IReadOnlyList<ClientSource> Sources => sources;

    /// <summary>
    /// Adds a Client Source location to the ClientFileManager
    /// </summary>
    /// <param name="pathName">Path to a Directory or game_pak file</param>
    /// <returns></returns>
    public static bool AddSource(string pathName)
    {
        if (string.IsNullOrWhiteSpace(pathName))
            return false;

        try
        {
            var p = Path.GetFullPath(pathName);
            pathName = p;
        }
        catch
        {
            // ignored
        }

        // Source is a directory of unpacked client files ?
        if (Directory.Exists(pathName))
        {
            var newSource = new ClientSource { SourceType = ClientSourceType.Directory, PathName = pathName };
            if (newSource.Open())
            {
                sources.Add(newSource);
                Logger.Info($"Using Source [{sources.Count}:{newSource.SourceType}]: {pathName}");
                return true;
            }
        }

        // Source is a packed file ?
        if (File.Exists(pathName))
        {
            var newSource = new ClientSource() { SourceType = ClientSourceType.GamePak, PathName = pathName };
            if (newSource.Open())
            {
                sources.Add(newSource);
                Logger.Info($"Using Source [{sources.Count}:{newSource.SourceType}]: {pathName}");
                return true;
            }
            else
            {
                Logger.Error($"Source could not be opened, or is invalid: {pathName}");
                return false;
            }
        }

        Logger.Warn($"Client Source not found {pathName}");
        return false;
    }

    public static void ClearSources()
    {
        for (var i = sources.Count - 1; i >= 0; i--)
        {
            var source = sources[i];
            source.Close();
            sources.Remove(source);
        }
    }

    public static IEnumerable<string> ListSources()
    {
        foreach (var source in sources)
        {
            yield return source.PathName;
        }
    }

    /// <summary>
    /// Gets the first ClientSource that is valid for target fileName
    /// </summary>
    /// <param name="fileName">filename to search for</param>
    /// <returns>First found ClientSource, or null</returns>
    public static ClientSource GetFileSource(string fileName)
    {
        foreach (var source in sources)
        {
            if (source.FileExists(fileName))
                return source;
        }
        return null;
    }

    /// <summary>
    /// Checks if target file exists in any of the sources
    /// </summary>
    /// <param name="fileName">relative filename to check</param>
    /// <returns></returns>
    public static bool FileExists(string fileName)
    {
        var source = GetFileSource(fileName);
        return (source != null);
    }

    /// <summary>
    /// Grabs the target fileName as a Stream
    /// </summary>
    /// <param name="fileName"></param>
    /// <returns></returns>
    public static async Task<Stream> GetFileStreamAsync(string fileName)
    {
        var source = GetFileSource(fileName);
        if (source == null)
        {
            Logger.Trace($"GetFileStream({fileName}) not found");
            return null;
        }
        //Logger.Debug($"[{source.PathName}].GetFileStream({fileName})");
        return await source.GetFileStreamAsync(fileName);
    }

    public static async Task<string> GetFileAsStringAsync(string fileName)
    {
        var source = GetFileSource(fileName);
        if (source is null)
            return string.Empty;
        return await source.GetFileAsStringAsync(fileName);
    }

    public static void Initialize()
    {
        if (_initialized)
            return;

        ClearSources();
        foreach (var source in AppConfiguration.Instance.ClientData.Sources)
        {
            if (!AddSource(source))
                Logger.Warn($"{source} is not a valid source for client data");
        }
        if (Sources.Count <= 0)
            Logger.Error("No valid client sources have been defined or found, some features will not work !");

        _initialized = true;
    }

    /// <summary>
    /// Create a list of filenames found in target directory
    /// </summary>
    /// <param name="directory">Directory to search in</param>
    /// <param name="searchPattern">search pattern to use</param>
    /// <param name="includeSubDirectories"></param>
    /// <returns>List of filenames</returns>
    public static List<string> GetFilesInDirectory(string directory, string searchPattern, bool includeSubDirectories)
    {
        var list = new List<string>();
        var baseList = new List<string>(); // Helper
        foreach (var source in sources)
        {
            var filesList = source.GetFilesInDirectory(directory, searchPattern, includeSubDirectories);
            foreach (var fName in filesList)
            {
                var baseName = source.GetBaseName(fName);
                if (!baseList.Contains(baseName))
                {
                    list.Add(fName);
                    baseList.Add(baseName);
                }
            }
        }
        return list;
    }
}
