using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.AI.v2.Controls;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class AiPathsManager : Singleton<AiPathsManager>
{
    private readonly string PathFileFolder;
    private const string PathFileExt = ".path";
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly object _lock = new();

    public AiPathsManager()
    {
        PathFileFolder = Path.Combine("Data", "Path");
    }

    /// <summary>
    /// Cache for loaded Path
    /// </summary>
    private Dictionary<string, List<AiPathPoint>> PathsCache { get; set; } = [];

    public List<AiPathPoint> LoadAiPathPoints(string aiPathFileName)
    {
        // If cached, return that
        lock (_lock)
        {
            if (PathsCache.TryGetValue(aiPathFileName, out var res))
                return res;
        }

        // Otherwise, try to load from file
        lock (_lock)
        {
            var res = new List<AiPathPoint>();
            try
            {
                var fullPathFileName = Path.Combine(PathFileFolder, aiPathFileName + PathFileExt);
                if (!File.Exists(fullPathFileName))
                    return res;

                var lines = File.ReadAllLines(fullPathFileName);
                // var lines = FileManager.GetFileContents(fullPathFileName);

                foreach (var line in lines)
                {
                    var columns = line.Split('|');
                    if (columns.Length != 5)
                        continue;
                    if (!float.TryParse(columns[1], out var x))
                        x = 0f;
                    if (!float.TryParse(columns[2], out var y))
                        y = 0f;
                    if (!float.TryParse(columns[3], out var z))
                        z = 0f;
                    var param = columns[4];

                    if (!Enum.TryParse<AiPathPointAction>(columns[0], true, out var action))
                        action = AiPathPointAction.None;

                    var newPoint = new AiPathPoint()
                    {
                        Position = new Vector3(x, y, z),
                        Action = action,
                        Param = param
                    };

                    res.Add(newPoint);
                }

                PathsCache.TryAdd(aiPathFileName, res);
            }
            catch (Exception e)
            {
                Logger.Error($"LoadAiPathPoint({aiPathFileName}), Exception: {e.Message}");
                res.Clear();
            }
            return res;
        }
    }

    /// <summary>
    /// Caches all path files in the Data/Paths folder (subfolders not included)
    /// </summary>
    public void Load()
    {
        var pathFiles = Directory.GetFiles(PathFileFolder, "*" + PathFileExt, SearchOption.TopDirectoryOnly);
        var loaded = 0;
        foreach (var pathFile in pathFiles)
        {
            var shortName = Path.GetFileNameWithoutExtension(pathFile);
            if (LoadAiPathPoints(shortName).Count > 0)
                loaded++;
        }
        Logger.Info($"Loaded {loaded} path files from {PathFileFolder}");
    }

    /// <summary>
    /// Removes all cached paths
    /// </summary>
    public void ClearCache()
    {
        lock (_lock)
        {
            PathsCache.Clear();
        }
    }
}
