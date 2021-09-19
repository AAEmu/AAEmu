using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using AAEmu.Game.Models;
using AAEmu.Commons.Utils.AAPak;
using NLog;
using SQLitePCL;

namespace AAEmu.Game.IO
{
    public enum ClientSourceType
    {
        Directory,
        GamePak
    }
    public class ClientSource
    {
        public ClientSourceType SourceType { get; set; }
        public string PathName { get; set; } // Directory Name or game_pak File Name
        public AAPak GamePak { get; set; }

        public bool Open()
        {
            switch (SourceType)
            {
                case ClientSourceType.Directory when (Directory.Exists(PathName)):
                    return true;
                case ClientSourceType.GamePak:
                    GamePak = new AAPak(PathName);
                    // GamePak.GenerateFolderList();
                    return GamePak.isOpen;
                default:
                    return false;
            }
        }

        public void Close()
        {
            if ((SourceType == ClientSourceType.GamePak) && (GamePak != null))
            {
                GamePak.ClosePak();
            }
        }

        /// <summary>
        /// Checks if target file exists for this source
        /// </summary>
        /// <param name="fileName">filename to check</param>
        /// <returns></returns>
        public bool FileExists(string fileName)
        {
            switch (SourceType)
            {
                case ClientSourceType.Directory:
                    {
                        var fn = Path.Combine(PathName, fileName);
                        return File.Exists(fn);
                    }
                case ClientSourceType.GamePak:
                    return GamePak.FileExists(fileName);
                default:
                    return false;
            }
        }
        
        public static string WildcardToRegex(string pattern)
        {
            return "^" + Regex.Escape(pattern).
                Replace("\\*", ".*").
                Replace("\\?", ".") + "$";
        }        
        
        /// <summary>
        /// Create a list of filenames found in target directory
        /// </summary>
        /// <param name="directory">Directory to search in</param>
        /// <param name="searchPattern">search pattern to use</param>
        /// <param name="includeSubDirectories"></param>
        /// <returns>List of filenames</returns>
        public List<string> GetFilesInDirectory(string directory, string searchPattern, bool includeSubDirectories)
        {
            var list = new List<string>();
            switch (SourceType)
            {
                case ClientSourceType.Directory:
                    {
                        var rootDir = directory.Replace('/', Path.DirectorySeparatorChar);
                        try
                        {
                            var files = Directory.GetFiles(Path.Combine(PathName, rootDir), searchPattern,
                                includeSubDirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
                            list.AddRange(files);
                        }
                        catch
                        {
                            // ignored
                        }
                        break;
                    }
                case ClientSourceType.GamePak:
                    {
                        var rootDir = directory.Replace('/',Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/').ToLower();
                        var wildCard = WildcardToRegex("*" + searchPattern.ToLower() + "*");// Hopefully this behaves the same as the Directory.GetFiles pattern
                        if (includeSubDirectories)
                        {
                            foreach (var pfi in GamePak.files)
                            {
                                if (pfi.name.ToLower().StartsWith(rootDir))
                                {
                                    if ((string.IsNullOrWhiteSpace(searchPattern) ||
                                         (Regex.Match(pfi.name.ToLower(), wildCard).Success)))
                                        list.Add(pfi.name);
                                }
                            }
                        }
                        else
                        {
                            var files = GamePak.GetFilesInDirectory(rootDir);
                            foreach (var pfi in files)
                                if ((string.IsNullOrWhiteSpace(searchPattern) || (Regex.Match(pfi.name, wildCard).Success)))
                                    list.Add(pfi.name);
                        }
                        break;
                    }
            }
            
            return list;
        }

        /// <summary>
        /// Grabs the target fileName as a Stream
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public Stream GetFileStream(string fileName)
        {
            switch (SourceType)
            {
                case ClientSourceType.Directory:
                    {
                        var fn = Path.Combine(PathName, fileName);
                        var fStream = new FileStream(fn, FileMode.Open, FileAccess.Read, FileShare.Read);
                        return fStream;
                    }
                case ClientSourceType.GamePak:
                    return GamePak.ExportFileAsStream(fileName);
                default:
                    return null;
            }
        }

        public string GetFileAsString(string fileName)
        {
            try
            {
                using (var stream = GetFileStream(fileName))
                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
            catch
            {
                return string.Empty;
            }
        }

    }
    
    public static class ClientFileManager
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        
        static List<ClientSource> Sources = new List<ClientSource>();

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
                    Sources.Add(newSource);
                    _log.Info($"Using Source [{Sources.Count}:{newSource.SourceType}]: {pathName}");
                    return true;
                }
            }

            // Source is a packed file ?
            if (File.Exists(pathName))
            {
                var newSource = new ClientSource() { SourceType = ClientSourceType.GamePak, PathName = pathName };
                if (newSource.Open())
                {
                    Sources.Add(newSource);
                    _log.Info($"Using Source [{Sources.Count}:{newSource.SourceType}]: {pathName}");
                    return true;
                }
                else
                {
                    _log.Error($"Source could not be opened, or is invalid: {pathName}");
                    return false;
                }
            }

            _log.Warn($"Client Source not found {pathName}");
            return false;
        }

        public static void ClearSources()
        {
            for (var i = Sources.Count - 1; i >= 0; i--)
            {
                var source = Sources[i] ;
                source.Close();
                Sources.Remove(source);
            }
        }

        public static List<string> ListSources()
        {
            var list = new List<string>();
            foreach (var source in Sources)
                list.Add(source.PathName);
            return list;
        }

        /// <summary>
        /// Gets the first ClientSource that is valid for target fileName
        /// </summary>
        /// <param name="fileName">filename to search for</param>
        /// <returns>First found ClientSource, or null</returns>
        public static ClientSource GetFileSource(string fileName)
        {
            foreach (var source in Sources)
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
        public static Stream GetFileStream(string fileName)
        {
            var source = GetFileSource(fileName);
            if (source == null)
            {
                _log.Debug($"GetFileStream({fileName}) not found");
                return null;
            }
            _log.Debug($"[{source.PathName}].GetFileStream({fileName})");
            return source.GetFileStream(fileName);
        }

        public static string GetFileAsString(string fileName)
        {
            var source = GetFileSource(fileName);
            if (source == null)
                return string.Empty;
            return source.GetFileAsString(fileName);
        }

        public static void Initialize()
        {
            AddSource(AppConfiguration.Instance.ClientDirectory);
            AddSource(AppConfiguration.Instance.ClientGamePak);
            if (ListSources().Count <= 0)
                _log.Error("No client sources have been defined !");
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
            // TODO: remove duplicate entries
            foreach (var source in Sources)
                list.AddRange(source.GetFilesInDirectory(directory,searchPattern,includeSubDirectories));
            return list;
        }
    }
}
