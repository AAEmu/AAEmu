using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AAEmu.Game.IO;
using Extensions;

namespace CgfConverter.PackFileSystem;

/// <summary>
/// Wrapper for ClientFileManager that CgfConverter uses
/// </summary>
public class AaGamePakFileSystem: IPackFileSystem
{
    private readonly string _rootPath = ClientFileManager.Sources[0]?.PathName ?? string.Empty;

    public Stream GetStream(string path)
    {
        using var s = ClientFileManager.GetFileStream(path);
        if (s == null)
            return null;
        var ms = new MemoryStream();
        s.CopyTo(ms);
        ms.Seek(0, SeekOrigin.Begin);
        return ms;
    }

    public bool Exists(string path)
    {
        return ClientFileManager.FileExists(path);;
    }

    public byte[] ReadAllBytes(string path)
    {
        return ClientFileManager.GetFileStream(path)?.ReadAllBytes() ?? [];
    }

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
    
}
