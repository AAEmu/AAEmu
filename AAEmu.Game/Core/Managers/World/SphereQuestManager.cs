using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using AAEmu.Commons.Utils;
using AAEmu.Game.GameData;
using AAEmu.Game.IO;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;
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
    private List<SphereQuestStarter> _questStartingSpheres;
    private List<SphereQuestStarter> _questSpheresBasic;
    // PlayerId, Pos
    private Dictionary<uint, Vector3> _questStartingLastPositionChecks;

    private object _addLock = new();
    private object _remLock = new();

    public SphereQuestManager()
    {
        _sphereQuestTriggers = [];
        _addQueue = [];
        _removeQueue = [];
        _questStartingSpheres = [];
        _questSpheresBasic = [];
        _questStartingLastPositionChecks = [];
    }

    public void Initialize()
    {
        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(500), true);
    }

    public void Load()
    {
        // Load sphere data
        _sphereQuests = LoadQuestSpheres();

        // Link quest starters to spheres
        _questStartingSpheres.Clear();
        foreach (var (componentId, sphereQuestList) in _sphereQuests)
        {
            // Get the relevant QuestComponentTemplate
            var questComponent = QuestManager.Instance.GetComponent(componentId);
            if (questComponent == null)
                continue;

            var sphereIdToAdd = SphereGameData.Instance.GetSphereIdFromQuest(questComponent.ParentQuestTemplate.Id);
            if (sphereIdToAdd <= 0)
                continue;

            foreach (var sphereQuest in sphereQuestList)
            {
                var newSphere = new SphereQuestStarter();
                newSphere.Sphere = sphereQuest;
                newSphere.QuestTemplateId = questComponent.ParentQuestTemplate.Id;
                newSphere.SphereId = sphereIdToAdd;
                _questSpheresBasic.Add(newSphere);

                foreach (var actTemplate in questComponent.ActTemplates)
                {
                    if (actTemplate is QuestActConAcceptSphere _)
                    {
                        _questStartingSpheres.Add(newSphere);
                    }
                }
            }
        }
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
        if (spheres != null)
        {
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
            // Add new player specific triggers
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
                _addQueue = [];
            }

            // Handle player specific Triggers
            foreach (var trigger in _sphereQuestTriggers)
            {
                if (trigger?.Owner?.Region?.HasPlayerActivity() ?? false)
                    trigger.Tick(delta);
            }

            // Remove player specific triggers
            lock (_remLock)
            {
                foreach (var triggerToRemove in _removeQueue)
                {
                    _sphereQuestTriggers.Remove(triggerToRemove);
                }

                _removeQueue = [];
            }

            // Handle Global triggers for quest starters
            foreach (var questStartingSphere in _questStartingSpheres)
            {
                if (questStartingSphere.Region == null)
                    questStartingSphere.Region = WorldManager.Instance.GetRegion(questStartingSphere.Sphere.ZoneId, questStartingSphere.Sphere.Xyz.X, questStartingSphere.Sphere.Xyz.Y);

                if (!questStartingSphere.Region?.HasPlayerActivity() ?? true)
                    continue;

                var playersInNearbyRegion = new Dictionary<uint, Character>();
                foreach (var region in questStartingSphere.Region.GetNeighbors())
                {
                    var playersInRegion = new List<Character>();
                    region.GetList(playersInRegion, 0);
                    foreach (var character in playersInRegion)
                        playersInNearbyRegion.TryAdd(character.Id, character);
                }

                foreach (var (characterId, character) in playersInNearbyRegion)
                {
                    var lastCheckLocation = _questStartingLastPositionChecks.GetValueOrDefault(characterId);
                    var isNew = lastCheckLocation == Vector3.Zero;
                    var oldInside = questStartingSphere.Sphere.Contains(lastCheckLocation);
                    var newInside = questStartingSphere.Sphere.Contains(character?.Transform?.World?.Position ?? Vector3.Zero);

                    if (!oldInside && newInside)
                    {
                        if (questStartingSphere.Sphere.DbSphere == null ||
                            UnitRequirementsGameData.Instance.CanTriggerSphere(questStartingSphere.Sphere.DbSphere, character))
                            QuestManager.Instance.DoOnEnterQuestStarterSphere(character, questStartingSphere, lastCheckLocation);
                    }
                    //else if (oldInside && !newInside)
                    //{
                    //    QuestManager.Instance.DoOnExitQuestStarterSphere(character, questStartingSphere, lastCheckLocation);
                    //}
                    var newPos = character?.Transform?.World?.Position ?? Vector3.Zero;
                    if (isNew)
                    {
                        _questStartingLastPositionChecks.TryAdd(characterId, newPos);
                    }
                    else
                    {
                        _questStartingLastPositionChecks[characterId] = newPos;
                    }
                }
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
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        var sphereQuests = new Dictionary<uint, List<SphereQuest>>();
        foreach (var world in worlds)
        {
            var worldLevelDesignDir = Path.Combine("game", "worlds", world.Name, "level_design", "zone");
            var pathFiles = ClientFileManager.GetFilesInDirectory(worldLevelDesignDir, "quest_sign_sphere.g", true);
            foreach (var pathFileName in pathFiles)
            {
                if (!uint.TryParse(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(pathFileName))), out var zoneId))
                {
                    Logger.Warn($"Unable to parse zoneId from {pathFileName}");
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
                            if (!sphereQuests.TryGetValue(sphere.ComponentId, out var value))
                            {
                                var sphereList = new List<SphereQuest>
                                {
                                    sphere
                                };
                                sphereQuests.Add(sphere.ComponentId, sphereList);
                            }
                            else
                            {
                                value.Add(sphere);
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

    public List<SphereQuest> GetSpheresForQuest(uint questSphereQuestId)
    {
        var res = new List<SphereQuest>();

        foreach (var questSpheres in _sphereQuests.Values)
            res.AddRange(questSpheres.Where(x => x.QuestId == questSphereQuestId).ToList());

        return res;
    }
}
