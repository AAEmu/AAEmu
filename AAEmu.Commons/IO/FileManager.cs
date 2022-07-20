using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AAEmu.Commons.IO
{
    /// <summary>
    /// Utility class for managing files and directories.
    /// </summary>
    public static class FileManager
    {
        #region AppPath

        private static string _appPath;

        /// <summary>Gets executing application directory.</summary>
        public static string AppPath
        {
            get
            {
                if (!string.IsNullOrEmpty(_appPath))
                    return _appPath;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.EntryPoint == null)
                        continue;
                    _appPath = Path.GetDirectoryName(new Uri(assembly.Location).LocalPath);
                    break;
                }

                if (_appPath != null &&
                    _appPath.EndsWith(Path.DirectorySeparatorChar.ToString(CultureInfo.InvariantCulture)) == false)
                    _appPath += Path.DirectorySeparatorChar;

                return _appPath;
            }
        }

        #endregion // AppPath

        #region SaveFile

        /// <summary>
        /// Saves a UTF8-string to the file.
        /// </summary>
        /// <param name="content">Content of the file.</param>
        /// <param name="file">File name to save this as (should include directories if applicable).</param>
        /// <param name="append">Tells the system if you wish to append data or create a new document.</param>
        public static void SaveFile(string content, string file, bool append = false)
        {
            SaveFile(Encoding.UTF8.GetBytes(content), file, append);
        }

        /// <summary>
        /// Saves a byte array to the file.
        /// </summary>
        /// <param name="content">Content of the file.</param>
        /// <param name="file">File name to save this as (should include directories if applicable).</param>
        /// <param name="append">Tells the system if you wish to append data or create a new document.</param>
        public static void SaveFile(byte[] content, string file, bool append = false)
        {
            FileStream writer = null;
            try
            {
                var index = file.LastIndexOf(Path.DirectorySeparatorChar);
                if (index <= 0)
                    index = file.LastIndexOf(Path.AltDirectorySeparatorChar);
                if (index <= 0)
                    throw new Exception("Directory must be specified for the file");

                var directory = file.Remove(index) + Path.DirectorySeparatorChar;
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                var opened = false;
                while (!opened)
                {
                    writer = File.Open(file, append ? FileMode.Append : FileMode.Create, FileAccess.Write,
                        FileShare.None);
                    opened = true;
                }

                writer.Write(content, 0, content.Length);
                writer.Close();
            }
            finally
            {
                if (writer != null)
                {
                    writer.Close();
                    writer.Dispose();
                }
            }
        }

        #endregion // SaveFile

        #region GetFileContents

        /// <summary>
        /// Gets a files' contents.
        /// </summary>
        /// <param name="file">File name</param>
        /// <param name="timeOut">Amount of time in ms to wait for the file</param>
        /// <returns>a string containing the file's contents</returns>
        public static string GetFileContents(string file, int timeOut = 5000)
        {
            StreamReader reader = null;
            var startTime = Environment.TickCount;
            try
            {
                if (!File.Exists(file))
                    return string.Empty;

                var opened = false;
                while (!opened)
                {
                    if (Environment.TickCount - startTime >= timeOut)
                        throw new IOException("File opening timed out");
                    reader = File.OpenText(file);
                    opened = true;
                }

                var contents = reader.ReadToEnd();
                reader.Close();
                return contents;
            }
            finally
            {
                if (reader != null)
                {
                    reader.Close();
                    reader.Dispose();
                }
            }
        }

        #endregion // GetFileContents

        public static string[] GetMatchingDirectories(string path, string mask, bool recursive = true)
        {
            return Directory.Exists(path)
                ? Directory.GetDirectories(path, mask,
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                : new string[0];
        }

        public static string[] GetMatchingFilesInDirectory(string path, string mask, bool recursive = true)
        {
            return Directory.Exists(path)
                ? Directory.GetFiles(path, mask,
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                : new string[0];
        }

        public static string[] GetMatchingFilesInDirectory(string path, Regex regexp, bool recursive = true)
        {
            return Directory.Exists(path)
                ? Directory.GetFiles(path, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                    .Where(f => regexp.IsMatch(f)).ToArray()
                : new string[0];
        }
    }
}
