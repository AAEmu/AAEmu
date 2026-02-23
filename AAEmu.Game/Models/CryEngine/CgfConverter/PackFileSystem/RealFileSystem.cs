using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Extensions;

namespace CgfConverter.PackFileSystem;

public class RealFileSystem : IPackFileSystem
{
    // Object dir
    private readonly string _rootPath;

    public RealFileSystem(string rootPath)
    {
        rootPath = Path.GetFullPath(rootPath);
        if (!Directory.Exists(rootPath))
            throw new FileNotFoundException();

        _rootPath = FileHandlingExtensions.CombineAndNormalizePath(rootPath) + "\\";
    }

    public Stream GetStream(string path)
    {
        try
        {
            return new FileStream(
                FileHandlingExtensions.CombineAndNormalizePath(_rootPath, path),
                FileMode.Open,
                FileAccess.Read);
        }
        catch (IOException ioe) when (ioe.HResult == unchecked((int) 0x8007007B)) // Path name is invalid
        {
            throw new FileNotFoundException(path);
        }
    }

    // TODO: Rework this.  
    public bool Exists(string path) =>
        File.Exists(FileHandlingExtensions.CombineAndNormalizePath(_rootPath, path));

    public string[] Glob(string pattern)
    {
        // remainingPattern always contains fully qualified path, but in lowercase.
        var remainingPatterns = new List<string>
        {
            Regex.Replace(FileHandlingExtensions.CombineAndNormalizePath(_rootPath, pattern), "[/\\\\]+", "\\")
        };
        var testedPatterns = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
        var foundPaths = new List<string>();

        while (remainingPatterns.Any())
        {
            pattern = remainingPatterns[^1].ToLowerInvariant();
            remainingPatterns.RemoveAt(remainingPatterns.Count - 1);
            if (testedPatterns.Contains(pattern))
                continue;
            testedPatterns.Add(pattern);

            if (!pattern.StartsWith(_rootPath, StringComparison.InvariantCultureIgnoreCase))
                continue;

            if (File.Exists(pattern))
                foundPaths.Add(pattern[_rootPath.Length..]);

            for (var i = 0; i < pattern.Length;)
            {
                var next = pattern.IndexOf('\\', i + 1);
                if (next == -1)
                    next = pattern.Length;

                int pos;
                if (-1 != (pos = pattern.IndexOf("**", i, next - i, StringComparison.Ordinal)))
                {
                    var searchBase = pattern[..i];
                    var prefix = pattern[(i + 1)..pos];
                    var suffix = pattern[pos..next].TrimStart('*');

                    var remainingPattern = next == pattern.Length ? string.Empty : pattern[(next + 1)..];
                    if (!remainingPattern.Contains('\\'))
                    {
                        try
                        {
                            remainingPatterns.AddRange(
                                Directory.GetFiles(searchBase, $"{prefix}*{suffix}{remainingPattern}"));
                        }
                        catch (Exception)
                        {
                            // pass
                        }
                    }

                    remainingPattern = $"**{pattern[(pos + 1)..]}";
                    try
                    {
                        remainingPatterns.AddRange(
                            Directory.GetDirectories(searchBase, $"{prefix}*")
                                .Select(x => Path.Join(x, remainingPattern)));
                    }
                    catch (Exception)
                    {
                        // pass
                    } 
                    break;
                }

                if (-1 != pattern.IndexOfAny(new[] {'?', '*'}, i, next - i))
                {
                    var searchBase = pattern[..i];
                    var remainingPattern = pattern[next..];

                    try {
                        remainingPatterns.AddRange(
                            Directory.GetFileSystemEntries(searchBase, pattern[i..next])
                                .Select(x => Path.Join(searchBase, x) + remainingPattern));
                    }
                    catch (Exception)
                    {
                        // pass
                    }
                    break;
                }

                i = next;
            }
        }

        return foundPaths.ToArray();
    }

    public byte[] ReadAllBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(Path.Join(_rootPath, path));
        }
        catch (FileNotFoundException)
        {
            throw new FileNotFoundException(path);
        }
        catch (DirectoryNotFoundException)
        {
            throw new FileNotFoundException(path);
        }
        catch (IOException ioe) when (ioe.HResult == unchecked((int) 0x8007007B)) // Path name is invalid
        {
            throw new FileNotFoundException(path);
        }
    }
}
