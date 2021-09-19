using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using AAEmu.Commons.Utils.AAPak;
using NLog;

namespace AAEmu.Commons.IO
{
    public enum ClientSourceType
    {
        File,
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
                case ClientSourceType.File when (Directory.Exists(PathName)):
                    return true;
                case ClientSourceType.GamePak:
                    GamePak = new AAPak(PathName);
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
                case ClientSourceType.File:
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

        /// <summary>
        /// Grabs the target fileName as a Stream
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public Stream GetFileStream(string fileName)
        {
            switch (SourceType)
            {
                case ClientSourceType.File:
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
            // Source is a directory of unpacked client files ?
            if (Directory.Exists(pathName))
            {
                var newSource = new ClientSource { SourceType = ClientSourceType.File, PathName = pathName };
                if (newSource.Open())
                {
                    Sources.Add(newSource);
                    _log.Info($"Using Client Source [{Sources.Count}:Directory]: {pathName}");
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
                    _log.Info($"Using Client Source [{Sources.Count}:Pak]: {pathName}");
                    return true;
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
                return null;
            return source.GetFileStream(fileName);
        }

        public static string GetFileAsString(string fileName)
        {
            var source = GetFileSource(fileName);
            if (source == null)
                return string.Empty;
            return source.GetFileAsString(fileName);
        }
        
    }
}
