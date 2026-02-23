using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CgfConverter;
using CgfConverter.PackFileSystem;

namespace Extensions;

public static class FileHandlingExtensions
{
    /// <summary>Material texture file extensions used to search resolve texture paths to files on disk</summary>
    private static readonly string?[] TextureExtensions = {null, ".dds", ".png", ".tif"};

    /// <summary>Attempts to resolve a material path to the correct file extension, and normalizes the path separators</summary>
    public static string ResolveTextureFile(string imagePath, IPackFileSystem fs, List<string> dataDirs)
    {
        foreach (var ext in TextureExtensions)
        {
            string testPath;
            var img = ext == null ? imagePath : Path.ChangeExtension(imagePath, ext);

            if (dataDirs is not null && dataDirs.Count > 0)
            {
                foreach (var dataDir in dataDirs)
                {
                    var texturePath = Path.Combine(dataDir, img);
                    if (fs.Exists(texturePath))
                        return texturePath.Replace('\\', '/');
                    if (fs.Exists(texturePath + ".1"))  // Armored warfare split dds files
                        return texturePath.Replace('\\', '/') + ".1";
                }
            }

            if (fs.Exists(testPath = img))
                return testPath.Replace('\\', '/');

            if (fs.Exists(testPath = Path.GetFileName(img)))
                return testPath.Replace('\\', '/');

            if (fs.Exists(testPath = Path.Combine("textures", Path.GetFileName(img))))
                return testPath.Replace('\\', '/');
        }

        if (fs.Glob($"**/{Path.GetFileNameWithoutExtension(imagePath)}.*")
                .FirstOrDefault(x => TextureExtensions.Contains(Path.GetExtension(x)?.ToLowerInvariant())) is { } path)
            return path;

        Utilities.Log(LogLevelEnum.Debug, $"Could not find extension for material texture \"{imagePath}\". Defaulting to .dds");
        
        return Path.ChangeExtension(imagePath, ".dds").Replace("\\", "/");
    }

    /// <summary>
    /// Combines and normalizes path components that may not exist yet in the local filesystem.
    /// </summary>
    /// <param name="pathComponents">Path fragments, which may include path separators.</param>
    /// <returns>Normalized path.</returns>
    public static string CombineAndNormalizePath(params string?[] pathComponents)
    {
        var parts = pathComponents.Where(x => x is not null).SelectMany(x => x!.Split('/', '\\')).ToList();
        for (var i = 0; i < parts.Count;)
        {
            if (parts[i] == "." || string.IsNullOrWhiteSpace(parts[i]))
            {
                parts.RemoveAt(i);
                continue;
            }

            parts[i] = parts[i].Trim();
            if (parts[i].Count(x => x == '.') == parts[i].Length)
            {
                parts.RemoveAt(i);
                if (i <= 0) 
                    continue;

                parts.RemoveAt(i - 1);
                i--;
                continue;
            }

            i++;
        }

        return string.Join('\\', parts);
    }
}
