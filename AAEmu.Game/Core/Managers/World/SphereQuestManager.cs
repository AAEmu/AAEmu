using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using AAEmu.Commons.Utils;
using AAEmu.Game.IO;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
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
        _sphereQuests = LoadQuestSpheres();
    }

    public void AddSphereQuestTrigger(SphereQuestTrigger trigger)
    {
        lock (_addLock)
        {
            _addQueue.Add(trigger);
        }
    }

    public int AddSphereQuestTriggers(ICharacter owner, Quest quest, uint componentId, uint npcTemplateId)
    {
        var res = 0;
        var spheres = GetQuestSpheres(componentId);
        foreach (var sphere in spheres)
        {
            var sphereQuestTrigger = new SphereQuestTrigger
            {
                Quest = quest,
                Owner = owner,
                Sphere = sphere,
                TickRate = 500,
                NpcTemplate = npcTemplateId
            };
            AddSphereQuestTrigger(sphereQuestTrigger);
            res++;
        }
        return res;
    }

    public void RemoveSphereQuestTrigger(SphereQuestTrigger trigger)
    {
        lock (_remLock)
        {
            _removeQueue.Add(trigger);
        }
    }

    /// <summary>
    /// Removes all Sphere triggers for a specified player and quest
    /// </summary>
    /// <param name="ownerId">Player ID</param>
    /// <param name="questId">Quest to remove, use zero for all triggers of this player</param>
    public void RemoveSphereQuestTriggers(uint ownerId, uint questId)
    {
        foreach (var questTrigger in _sphereQuestTriggers)
        {
            if ((questTrigger.Owner.Id == ownerId) && ((questId == 0) || (questTrigger.Quest.TemplateId == questId)))
                RemoveSphereQuestTrigger(questTrigger);
        }
    }

    private void Tick(TimeSpan delta)
    {
        try
        {
            // Add new triggers
            lock (_addLock)
            {
                if (_addQueue?.Count > 0)
                {
                    foreach (var addQuestSphereTrigger in _addQueue)
                    {
                        foreach (var sphereQuestTrigger in _sphereQuestTriggers)
                        {
                            if ((addQuestSphereTrigger.Owner.Id == sphereQuestTrigger.Owner.Id) &&
                                (addQuestSphereTrigger.Quest.TemplateId == sphereQuestTrigger.Quest.TemplateId))
                                break;
                        }

                        _sphereQuestTriggers.Add(addQuestSphereTrigger);
                    }
                }
                // Erase the list again for next tick
                _addQueue = new List<SphereQuestTrigger>();
            }

            // Handle Triggers
            foreach (var trigger in _sphereQuestTriggers)
            {
                if (trigger?.Owner?.Region?.HasPlayerActivity() ?? false)
                    trigger.Tick(delta);
            }

            // Remove triggers
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

    public List<SphereQuest> GetQuestSpheres(uint componentId)
    {
        return _sphereQuests.GetValueOrDefault(componentId);
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
                            var subLine = l3.Substring(4).Replace("(", "").Replace(")", "").Replace("x", "").Replace("y", "").Replace("z", "").Replace(" ", "");
                            var posString = subLine.Split(',');
                            if (posString.Length == 3)
                            {
                                // Parse the floats with NumberStyles.Float and CultureInfo.InvariantCulture or we get all sorts of 
                                // weird stuff with the decimal points depending on the user's language settings
                                var sphereX = float.Parse(posString[0], NumberStyles.Float, CultureInfo.InvariantCulture);
                                var sphereY = float.Parse(posString[1], NumberStyles.Float, CultureInfo.InvariantCulture);
                                var sphereZ = float.Parse(posString[2], NumberStyles.Float, CultureInfo.InvariantCulture);
                                sphere.Xyz = new Vector3(sphereX, sphereY, sphereZ);
                            }
                            sphere.Radius = float.Parse(l4.AsSpan(7), NumberStyles.Float, CultureInfo.InvariantCulture);
                            // конвертируем координаты из локальных в мировые, сразу при считывании из файла пути
                            // convert coordinates from local to world, immediately when reading the path from the file
                            sphere.Xyz = ZoneManager.ConvertToWorldCoordinates(zoneId, sphere.Xyz);
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
