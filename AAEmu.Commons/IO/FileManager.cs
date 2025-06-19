using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace AAEmu.Commons.IO;

/// <summary>
/// 用于管理文件和目录的实用工具类。
/// </summary>
public static class FileManager
{
    #region AppPath

    private static string _appPath;

    /// <summary>获取正在执行的应用程序目录。</summary>
    public static string AppPath
    {
        get
        {
            if (!string.IsNullOrEmpty(_appPath))
                return _appPath;
            // 遍历当前应用程序域中的所有程序集，以查找入口点程序集。
            // 这是为了确定应用程序的根执行路径，尤其是在宿主环境复杂或通过不同方式启动时。
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.EntryPoint == null) // 入口点为 null 的通常是库或动态程序集。
                    continue;
                _appPath = Path.GetDirectoryName(new Uri(assembly.Location).LocalPath); // 获取入口程序集所在目录的路径。
                break; // 找到第一个入口点程序集后即停止。
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
    /// 将 UTF8 字符串保存到文件。
    /// </summary>
    /// <param name="content">文件内容。</param>
    /// <param name="file">要保存的文件名（如果适用，应包括目录）。</param>
    /// <param name="append">告知系统是附加数据还是创建新文档。</param>
    public static void SaveFile(string content, string file, bool append = false)
    {
        SaveFile(Encoding.UTF8.GetBytes(content), file, append);
    }

    /// <summary>
    /// 将字节数组保存到文件。
    /// </summary>
    /// <param name="content">文件内容。</param>
    /// <param name="file">要保存的文件名（如果适用，应包括目录）。</param>
    /// <param name="append">告知系统是附加数据还是创建新文档。</param>
    public static void SaveFile(byte[] content, string file, bool append = false)
    {
        FileStream writer = null;
        try
        {
            var index = file.LastIndexOf(Path.DirectorySeparatorChar);
            if (index <= 0)
                index = file.LastIndexOf(Path.AltDirectorySeparatorChar);

            if (index <= 0)
                throw new DirectoryNotFoundException("Directory must be specified for the file");

            var directory = file.Remove(index) + Path.DirectorySeparatorChar;
            // 确保目标目录存在，如果不存在则创建。
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var opened = false;
            // 尝试打开文件进行写入。此循环可能用于处理短暂的文件锁定情况，
            // 尽管没有明确的延迟或重试次数限制，这可能导致在持续锁定下出现问题。
            // FileShare.None 表示在写入期间不允许其他进程访问该文件。
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
    /// 获取文件内容。
    /// </summary>
    /// <param name="file">文件名</param>
    /// <param name="timeOut">等待文件的毫秒数</param>
    /// <returns>包含文件内容的字符串</returns>
    public static string GetFileContents(string file, int timeOut = 5000)
    {
        StreamReader reader = null;
        var startTime = Environment.TickCount;
        try
        {
            if (!File.Exists(file))
                return string.Empty;

            var opened = false;
            // 尝试打开文件进行读取，包含超时机制。
            // 此循环尝试打开文件，直到成功或达到指定的超时时间。
            while (!opened)
            {
                if (Environment.TickCount - startTime >= timeOut) // 检查是否已超过超时时间。
                    throw new IOException("File opening timed out");
                reader = File.OpenText(file); // 以文本方式打开文件。
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

    /// <summary>
    /// 获取指定路径下与提供的掩码匹配的目录。
    /// </summary>
    /// <param name="path">要搜索的目录路径。</param>
    /// <param name="mask">用于匹配目录名称的掩码（例如，“*test*”）。</param>
    /// <param name="recursive">如果为 true，则递归搜索所有子目录；否则仅搜索顶级目录。</param>
    /// <returns>包含匹配目录完整路径的字符串数组；如果路径不存在或没有匹配项，则为空数组。</returns>
    public static string[] GetMatchingDirectories(string path, string mask, bool recursive = true)
    {
        return Directory.Exists(path)
            ? Directory.GetDirectories(path, mask,
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();
    }

    /// <summary>
    /// 获取指定路径下与提供的掩码匹配的文件。
    /// </summary>
    /// <param name="path">要搜索的目录路径。</param>
    /// <param name="mask">用于匹配文件名称的掩码（例如，“*.txt”）。</param>
    /// <param name="recursive">如果为 true，则递归搜索所有子目录；否则仅搜索顶级目录。</param>
    /// <returns>包含匹配文件完整路径的字符串数组；如果路径不存在或没有匹配项，则为空数组。</returns>
    public static string[] GetMatchingFilesInDirectory(string path, string mask, bool recursive = true)
    {
        return Directory.Exists(path)
            ? Directory.GetFiles(path, mask,
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();
    }

    /// <summary>
    /// 获取指定路径下与提供的正则表达式匹配的文件。
    /// </summary>
    /// <param name="path">要搜索的目录路径。</param>
    /// <param name="regexp">用于匹配文件名称的正则表达式。</param>
    /// <param name="recursive">如果为 true，则递归搜索所有子目录；否则仅搜索顶级目录。</param>
    /// <returns>包含匹配文件完整路径的字符串数组；如果路径不存在或没有匹配项，则为空数组。</returns>
    public static string[] GetMatchingFilesInDirectory(string path, Regex regexp, bool recursive = true)
    {
        return Directory.Exists(path)
            ? Directory.GetFiles(path, "*", recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(f => regexp.IsMatch(f)).ToArray()
            : Array.Empty<string>();
    }
}
