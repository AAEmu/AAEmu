using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using AAEmu.Commons.Utils;
using AAEmu.Game.IO;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

public class SphereQuestManager : Singleton<SphereQuestManager>, ISphereQuestManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, List<SphereQuest>> _sphereQuests;

    private readonly List<SphereQuestTrigger> _sphereQuestTriggers;
    private List<SphereQuestTrigger> _addQueue;
    private List<SphereQuestTrigger> _removeQueue;

    private object _addLock = new();
    private object _remLock = new();

    public SphereQuestManager()
    {
        _sphereQuestTriggers = new List<SphereQuestTrigger>();
        _addQueue = new List<SphereQuestTrigger>();
        _removeQueue = new List<SphereQuestTrigger>();
    }

    public void Initialize()
    {
        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(500), true);
    }

    public void Load()
    {
        //_sphereQuests = LoadSphereQuests();
        _sphereQuests = LoadQuestSpheres();
    }

    public void AddSphereQuestTrigger(SphereQuestTrigger trigger)
    {
        lock (_addLock)
        {
            _addQueue.Add(trigger);
        }
    }

    public void RemoveSphereQuestTrigger(SphereQuestTrigger trigger)
    {
        lock (_remLock)
        {
            _removeQueue.Add(trigger);
        }
    }

    private void Tick(TimeSpan delta)
    {
        try
        {
            lock (_addLock)
            {
                if (_addQueue?.Count > 0)
                    _sphereQuestTriggers.AddRange(_addQueue);
                _addQueue = new List<SphereQuestTrigger>();
            }

            foreach (var trigger in _sphereQuestTriggers)
            {
                if (trigger?.Owner?.Region?.HasPlayerActivity() ?? false)
                    trigger?.Tick(delta);
            }

            lock (_remLock)
            {
                foreach (var triggerToRemove in _removeQueue)
                {
                    _sphereQuestTriggers.Remove(triggerToRemove);
                }

                _removeQueue = new List<SphereQuestTrigger>();
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error in SphereQuestTrigger tick !");
        }
    }

    /* Unused
    private static Dictionary<uint, SphereQuest> LoadSphereQuests()
    {
        var spheres = new List<SphereQuest>();
        var sphereQuests = new Dictionary<uint, SphereQuest>();

        var contents = FileManager.GetFileContents($"{FileManager.AppPath}Data/quest_sign_spheres.json");
        if (string.IsNullOrWhiteSpace(contents))
            Logger.Warn($"File {FileManager.AppPath}Data/quest_sign_spheres.json doesn't exists or is empty.");
        else
        {
            try
            {
                JsonHelper.TryDeserializeObject(contents, out spheres, out _);
                foreach (var sphere in spheres)
                {
                    if (sphereQuests.ContainsKey(sphere.ComponentID))
                        continue;

                    // конвертируем координаты из локальных в мировые, сразу при считывании из файла
                    var _xyz = new Vector3(sphere.X, sphere.Y, sphere.Z);
                    var xyz = ZoneManager.ConvertToWorldCoordinates(sphere.ZoneID, _xyz);
                    sphere.X = xyz.X;
                    sphere.Y = xyz.Y;
                    sphere.Z = xyz.Z;
                    sphereQuests.Add(sphere.ComponentID, sphere);
                }
            }
            catch (Exception ex)
            {
                throw new GameException($"SpawnManager: Parse {FileManager.AppPath}Data/quest_sign_spheres.json file. Reason: " + ex.Message, ex);
            }
        }

        return sphereQuests;
    }*/

    public List<SphereQuest> GetQuestSpheres(uint componentId)
    {
        return _sphereQuests.TryGetValue(componentId, out var sphereQuests) ? sphereQuests : null;
    }

    public List<SphereQuestTrigger> GetSphereQuestTriggers()
    {
        return _sphereQuestTriggers;
    }

    /// <summary>
    /// LoadQuestSpheres by ZeromusXYZ
    /// Считываем все сферы из всех инстансов
    /// Read all spheres from all instances
    /// </summary>
    /// <returns></returns>
    private static Dictionary<uint, List<SphereQuest>> LoadQuestSpheres()
    {
        Logger.Info("Loading SphereQuest...");
        var worlds = WorldManager.Instance.GetWorlds();
        Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

        var sphereQuests = new Dictionary<uint, List<SphereQuest>>();
        foreach (var world in worlds)
        {
            var worldLevelDesignDir = Path.Combine("game", "worlds", world.Name, "level_design", "zone");
            var pathFiles = ClientFileManager.GetFilesInDirectory(worldLevelDesignDir, "quest_sign_sphere.g", true);
            foreach (var pathFileName in pathFiles)
            {
                if (!uint.TryParse(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(pathFileName))), out var zoneId))
                {
                    Logger.Warn("Unable to parse zoneId from {0}", pathFileName);
                    continue;
                }
                var contents = ClientFileManager.GetFileAsString(pathFileName);
                if (string.IsNullOrWhiteSpace(contents))
                {
                    Logger.Warn($"{pathFileName} doesn't exists or is empty.");
                    continue;
                }
                Logger.Debug($"Loading {pathFileName}");

                var area = contents.ToLower().Split('\n').ToList();

                for (var i = 0; i < area.Count - 4; i++)
                {
                    var l0 = area[i + 0].Trim(' ').Trim('\t').Trim('\r'); // area
                    var l1 = area[i + 1].Trim(' ').Trim('\t').Trim('\r'); // qtype
                    var l2 = area[i + 2].Trim(' ').Trim('\t').Trim('\r'); // ctype
                    var l3 = area[i + 3].Trim(' ').Trim('\t').Trim('\r'); // pos
                    var l4 = area[i + 4].Trim(' ').Trim('\t').Trim('\r'); // radius
                    if (l0.StartsWith("area") && l1.StartsWith("qtype") && l2.StartsWith("ctype") && l3.StartsWith("pos") && l4.StartsWith("radius"))
                    {
                        try
                        {
                            var sphere = new SphereQuest();
                            sphere.WorldId = world.Name;
                            sphere.ZoneId = zoneId;
                            sphere.QuestId = uint.Parse(l1.Substring(6));
                            sphere.ComponentId = uint.Parse(l2.Substring(6));
                            var subline = l3.Substring(4).Replace("(", "").Replace(")", "").Replace("x", "").Replace("y", "").Replace("z", "").Replace(" ", "");
                            var posstring = subline.Split(',');
                            if (posstring.Length == 3)
                            {
                                // Parse the floats with NumberStyles.Float and CultureInfo.InvariantCulture or we get all sorts of 
                                // weird stuff with the decimal points depending on the user's language settings
                                sphere.X = float.Parse(posstring[0], NumberStyles.Float, CultureInfo.InvariantCulture);
                                sphere.Y = float.Parse(posstring[1], NumberStyles.Float, CultureInfo.InvariantCulture);
                                sphere.Z = float.Parse(posstring[2], NumberStyles.Float, CultureInfo.InvariantCulture);
                            }
                            sphere.Radius = float.Parse(l4.AsSpan(7), NumberStyles.Float, CultureInfo.InvariantCulture);
                            // конвертируем координаты из локальных в мировые, сразу при считывании из файла пути
                            // convert coordinates from local to world, immediately when reading the path from the file
                            var xyz = new Vector3(sphere.X, sphere.Y, sphere.Z);
                            var vec = ZoneManager.ConvertToWorldCoordinates(zoneId, xyz);
                            sphere.X = vec.X;
                            sphere.Y = vec.Y;
                            sphere.Z = vec.Z;
                            if (!sphereQuests.ContainsKey(sphere.ComponentId))
                            {
                                var sphereList = new List<SphereQuest>();
                                sphereList.Add(sphere);
                                sphereQuests.Add(sphere.ComponentId, sphereList);
                            }
                            else
                            {
                                sphereQuests[sphere.ComponentId].Add(sphere);
                            }
                            i += 4;
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("Loading SphereQuest error!");
                            Logger.Fatal(ex);
                            throw;
                        }
                    }
                }
            }
        }
        return sphereQuests;
    }
}
