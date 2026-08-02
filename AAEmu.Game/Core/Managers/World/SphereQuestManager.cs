using System.Globalization;
using System.Numerics;
using AAEmu.Game.GameData;
using AAEmu.Game.IO;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.World;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

public class SphereQuestManager(WorldInstance parent) : ISphereQuestManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private static Dictionary<uint, List<SphereQuest>> _sphereQuests;
    /// <summary>zoneId → quest_area_sphere.g entries (stype = spheres.id).</summary>
    private static Dictionary<uint, List<SphereQuest>> _questAreaSpheres;

    private readonly List<SphereQuestTrigger> _sphereQuestTriggers = [];
    private List<SphereQuestTrigger> _addQueue = [];
    private List<SphereQuestTrigger> _removeQueue = [];
    private readonly List<SphereQuestStarter> _questStartingSpheres = [];
    private readonly List<SphereQuestStarter> _questSpheresBasic = [];
    // PlayerId, Pos
    private readonly Dictionary<uint, Vector3> _questStartingLastPositionChecks = [];

    private readonly object _addLock = new();
    private readonly object _remLock = new();
    private readonly object _questStartingSpheresLock = new();

    public void Initialize()
    {
        TickManager.Instance.OnTick.Subscribe(Tick, TimeSpan.FromMilliseconds(500), true);
    }

    public void Load()
    {
        // Load sphere data
        if (_sphereQuests == null)
            _sphereQuests = LoadQuestSpheres(parent.Template);
        if (_questAreaSpheres == null)
        {
            _questAreaSpheres = LoadQuestAreaSpheres(parent.Template);
            _questAreaSphereGrid = BuildQuestAreaSphereGrid(_questAreaSpheres);
        }

        // Link quest starters to spheres — build first, then swap atomically
        var newStartingSpheres = new List<SphereQuestStarter>();
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
                var newSphere = new SphereQuestStarter
                {
                    Sphere = sphereQuest, QuestTemplateId = questComponent.ParentQuestTemplate.Id, SphereId = sphereIdToAdd
                };
                _questSpheresBasic.Add(newSphere);

                foreach (var actTemplate in questComponent.ActTemplates)
                {
                    if (actTemplate is QuestActConAcceptSphere _)
                    {
                        newStartingSpheres.Add(newSphere);
                    }
                }
            }
        }

        lock (_questStartingSpheresLock)
        {
            _questStartingSpheres.Clear();
            _questStartingSpheres.AddRange(newStartingSpheres);
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
            if (questTrigger.Owner.Id == ownerId && (questId == 0 || questTrigger.Quest.TemplateId == questId))
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
                            if (addQuestSphereTrigger.Owner.Id == sphereQuestTrigger.Owner.Id &&
                                addQuestSphereTrigger.Quest.TemplateId == sphereQuestTrigger.Quest.TemplateId)
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
            List<SphereQuestStarter> startingSphereSnapshot;
            lock (_questStartingSpheresLock)
                startingSphereSnapshot = [.._questStartingSpheres];
            foreach (var questStartingSphere in startingSphereSnapshot)
            {
                // Link the region if it hasn't been done yet
                questStartingSphere.Region ??= parent.GetRegionByPos(questStartingSphere.Sphere.Xyz);

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

            // Position-diff SphereBuff / SphereAccept / quest_area spheres for players in this world.
            // ZWEnterArea alone misses "already standing in Ezi's Light" after login / teleport.
            foreach (var character in parent.GetAllCharacters())
            {
                if (character?.Quests == null)
                    continue;
                if (!(character.Region?.HasPlayerActivity() ?? false))
                    continue;
                try
                {
                    character.Quests.ReconcileQuestAreaSpheres();
                }
                catch (Exception ex)
                {
                    Logger.Warn(ex, "QuestAreaSphere reconcile failed for {0}", character.Name);
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

    /// <summary>Cell edge in world units for <see cref="_questAreaSphereGrid"/>.</summary>
    private const float SphereGridCell = 256f;

    /// <summary>
    /// World-space grid over every zone's quest_area_sphere.g volume, each sphere registered in all
    /// cells its bounding box touches. Built once from <see cref="_questAreaSpheres"/>.
    /// </summary>
    private static Dictionary<(int X, int Y), List<SphereQuest>> _questAreaSphereGrid;

    private static (int X, int Y) SphereGridCellOf(float x, float y) =>
        ((int)MathF.Floor(x / SphereGridCell), (int)MathF.Floor(y / SphereGridCell));

    /// <summary>
    /// These volumes are authored per zone file but live in world space and routinely overhang the
    /// zone border — Two Crowns' dock sphere (spheres.id 2313) is 500 m wide, so a ship leaving the
    /// harbour crosses into the neighbouring zone while still deep inside the circle the map draws.
    /// A per-zone lookup dropped Ezi's Divine Protection / Moored at that border, which is why dock
    /// repair stopped a few boat lengths out. Indexing by position instead makes the volume, not the
    /// zone it was authored in, decide membership.
    /// </summary>
    private static Dictionary<(int X, int Y), List<SphereQuest>> BuildQuestAreaSphereGrid(
        Dictionary<uint, List<SphereQuest>> spheresByZone)
    {
        var grid = new Dictionary<(int X, int Y), List<SphereQuest>>();
        if (spheresByZone == null)
            return grid;

        foreach (var list in spheresByZone.Values)
        {
            foreach (var sphere in list)
            {
                var (minX, minY) = SphereGridCellOf(sphere.Xyz.X - sphere.Radius, sphere.Xyz.Y - sphere.Radius);
                var (maxX, maxY) = SphereGridCellOf(sphere.Xyz.X + sphere.Radius, sphere.Xyz.Y + sphere.Radius);
                for (var cellX = minX; cellX <= maxX; cellX++)
                {
                    for (var cellY = minY; cellY <= maxY; cellY++)
                    {
                        if (!grid.TryGetValue((cellX, cellY), out var cell))
                            grid[(cellX, cellY)] = cell = [];
                        cell.Add(sphere);
                    }
                }
            }
        }

        return grid;
    }

    /// <summary>
    /// quest_area_sphere.g volumes containing worldPos (stype → spheres.id), regardless of which
    /// zone file authored them.
    /// </summary>
    public IReadOnlyList<SphereQuest> GetContainingQuestAreaSpheres(uint zoneId, Vector3 worldPos)
    {
        var grid = _questAreaSphereGrid;
        if (grid == null || !grid.TryGetValue(SphereGridCellOf(worldPos.X, worldPos.Y), out var candidates))
            return [];

        List<SphereQuest> hits = null;
        foreach (var sphere in candidates)
        {
            if (!sphere.Contains(worldPos))
                continue;
            hits ??= [];
            hits.Add(sphere);
        }

        return (IReadOnlyList<SphereQuest>)(hits ?? []);
    }

    /// <summary>
    /// LoadQuestSpheres by ZeromusXYZ
    /// Считываем все сферы из всех инстансов
    /// Read all spheres from all instances
    /// </summary>
    /// <returns></returns>
    private static Dictionary<uint, List<SphereQuest>> LoadQuestSpheres(WorldTemplate worldTemplate)
    {
        Logger.Info("Loading SphereQuest...");
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        var sphereQuests = new Dictionary<uint, List<SphereQuest>>();
        var worldLevelDesignDir = Path.Combine("game", "worlds", worldTemplate.Name, "level_design", "zone");
        var pathFiles = ClientFileManager.GetFilesInDirectory(worldLevelDesignDir, "quest_sign_sphere.g", true);
        Logger.Debug($"Loading {pathFiles.Count} quest sign sphere data files");
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

            Logger.Trace($"Loading {pathFileName}");

            var area = contents.ToLower().Split('\n').ToList();

            for (var i = 0; i < area.Count - 4; i++)
            {
                var l0 = area[i + 0].Trim(' ').Trim('\t').Trim('\r'); // area
                var l1 = area[i + 1].Trim(' ').Trim('\t').Trim('\r'); // qtype
                var l2 = area[i + 2].Trim(' ').Trim('\t').Trim('\r'); // ctype
                var l3 = area[i + 3].Trim(' ').Trim('\t').Trim('\r'); // pos
                var l4 = area[i + 4].Trim(' ').Trim('\t').Trim('\r'); // radius
                if (l0.StartsWith("area") && l1.StartsWith("qtype") && l2.StartsWith("ctype") &&
                    l3.StartsWith("pos") && l4.StartsWith("radius"))
                {
                    try
                    {
                        var sphere = new SphereQuest
                        {
                            WorldId = worldTemplate.Name,
                            ZoneId = zoneId,
                            QuestId = uint.Parse(l1.Substring(6)),
                            ComponentId = uint.Parse(l2.Substring(6))
                        };
                        var subLine = l3.Substring(4).Replace("(", "").Replace(")", "").Replace("x", "")
                            .Replace("y", "").Replace("z", "").Replace(" ", "");
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
                        sphere.Xyz = ZoneManager.Instance.ConvertToWorldCoordinates(zoneId, sphere.Xyz);
                        if (!sphereQuests.TryGetValue(sphere.ComponentId, out var value))
                        {
                            var sphereList = new List<SphereQuest> { sphere };
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

        return sphereQuests;
    }

    /// <summary>
    /// Load zone <c>quest_area_sphere.g</c> (stype = compact spheres.id). Used when Zone
    /// reports ZWEnterArea group 16 — dedicate does not put sphere id on the wire.
    /// </summary>
    private static Dictionary<uint, List<SphereQuest>> LoadQuestAreaSpheres(WorldTemplate worldTemplate)
    {
        Logger.Info("Loading QuestAreaSphere...");
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

        var byZone = new Dictionary<uint, List<SphereQuest>>();
        var worldLevelDesignDir = Path.Combine("game", "worlds", worldTemplate.Name, "level_design", "zone");
        var pathFiles = ClientFileManager.GetFilesInDirectory(worldLevelDesignDir, "quest_area_sphere.g", true);

        // ClientData/pak may omit loose zone trees; also scan ZoneGameDataRoot / Server game.
        foreach (var extra in EnumerateLooseQuestAreaSphereFiles(worldTemplate.Name))
        {
            if (!pathFiles.Contains(extra, StringComparer.OrdinalIgnoreCase))
                pathFiles.Add(extra);
        }

        Logger.Debug("Loading {0} quest area sphere data files", pathFiles.Count);
        foreach (var pathFileName in pathFiles)
        {
            if (!TryParseZoneIdFromSpherePath(pathFileName, out var zoneId))
            {
                Logger.Warn("Unable to parse zoneId from {0}", pathFileName);
                continue;
            }

            var contents = pathFileName.Contains(':') || Path.IsPathRooted(pathFileName)
                ? (File.Exists(pathFileName) ? File.ReadAllText(pathFileName) : null)
                : ClientFileManager.GetFileAsString(pathFileName);
            // Loose absolute paths are not in ClientFileManager — read from disk.
            if (string.IsNullOrWhiteSpace(contents) && File.Exists(pathFileName))
                contents = File.ReadAllText(pathFileName);
            if (string.IsNullOrWhiteSpace(contents))
            {
                Logger.Warn("{0} doesn't exists or is empty.", pathFileName);
                continue;
            }

            var area = contents.ToLower().Split('\n').ToList();
            for (var i = 0; i < area.Count - 3; i++)
            {
                var l0 = area[i + 0].Trim(' ', '\t', '\r'); // area
                var l1 = area[i + 1].Trim(' ', '\t', '\r'); // kind
                var l2 = area[i + 2].Trim(' ', '\t', '\r'); // stype
                var l3 = area[i + 3].Trim(' ', '\t', '\r'); // pos
                var l4 = i + 4 < area.Count ? area[i + 4].Trim(' ', '\t', '\r') : ""; // radius
                if (!l0.StartsWith("area") || !l1.StartsWith("kind") || !l2.StartsWith("stype") ||
                    !l3.StartsWith("pos") || !l4.StartsWith("radius"))
                    continue;

                try
                {
                    var sphereId = uint.Parse(l2.AsSpan(6), CultureInfo.InvariantCulture);
                    var subLine = l3.Substring(4).Replace("(", "").Replace(")", "").Replace("x", "")
                        .Replace("y", "").Replace("z", "").Replace(" ", "");
                    var posString = subLine.Split(',');
                    if (posString.Length != 3)
                        continue;

                    var sphereX = float.Parse(posString[0], NumberStyles.Float, CultureInfo.InvariantCulture);
                    var sphereY = float.Parse(posString[1], NumberStyles.Float, CultureInfo.InvariantCulture);
                    var sphereZ = float.Parse(posString[2], NumberStyles.Float, CultureInfo.InvariantCulture);
                    var radius = float.Parse(l4.AsSpan(7), NumberStyles.Float, CultureInfo.InvariantCulture);
                    var xyz = ZoneManager.Instance.ConvertToWorldCoordinates(zoneId,
                        new Vector3(sphereX, sphereY, sphereZ));

                    uint questId = 0;
                    uint componentId = 0;
                    // QuestManager may not be fully linked at Load(); resolve on Zone enter.
                    _ = (questId, componentId);

                    var sphere = new SphereQuest
                    {
                        WorldId = worldTemplate.Name,
                        ZoneId = zoneId,
                        SphereId = sphereId,
                        QuestId = 0,
                        ComponentId = 0,
                        Xyz = xyz,
                        Radius = radius
                    };

                    if (!byZone.TryGetValue(zoneId, out var list))
                    {
                        list = [];
                        byZone[zoneId] = list;
                    }

                    list.Add(sphere);
                    i += 4;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Loading QuestAreaSphere error in {0}", pathFileName);
                }
            }
        }

        var total = byZone.Values.Sum(v => v.Count);
        Logger.Info("Loaded {0} quest_area spheres across {1} zones", total, byZone.Count);
        return byZone;
    }

    private static bool TryParseZoneIdFromSpherePath(string pathFileName, out uint zoneId)
    {
        zoneId = 0;
        // .../zone/<id>/world_server/quest_area_sphere.g  OR ClientFileManager relative
        var dir = Path.GetDirectoryName(pathFileName);
        var zoneDir = Path.GetDirectoryName(dir);
        return zoneDir != null && uint.TryParse(Path.GetFileName(zoneDir), out zoneId);
    }

    private static List<string> EnumerateLooseQuestAreaSphereFiles(string worldName)
    {
        var found = new List<string>();
        foreach (var root in EnumerateZoneGameDataRoots())
        {
            var zoneRoot = Path.Combine(root, "worlds", worldName, "level_design", "zone");
            if (!Directory.Exists(zoneRoot))
                continue;
            try
            {
                found.AddRange(Directory.GetFiles(zoneRoot, "quest_area_sphere.g", SearchOption.AllDirectories));
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "QuestAreaSphere: failed scanning {0}", zoneRoot);
            }
        }

        return found;
    }

    private static IEnumerable<string> EnumerateZoneGameDataRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Offer(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return;
            try
            {
                var full = Path.GetFullPath(candidate.Trim());
                if (Directory.Exists(full))
                    seen.Add(full);
            }
            catch
            {
                // ignore bad paths
            }
        }

        Offer(Environment.GetEnvironmentVariable("AAEMU_ZONE_GAME_DATA_ROOT"));
        // World always uses Server/game (same tree dedic.bat loads). Never client\game.
        Offer(@"G:\AAchina\Server\game");
        return seen;
    }

    public static List<SphereQuest> GetSpheresForQuest(uint questSphereQuestId)
    {
        var res = new List<SphereQuest>();

        foreach (var questSpheres in _sphereQuests.Values)
            res.AddRange(questSpheres.Where(x => x.QuestId == questSphereQuestId).ToList());

        return res;
    }
}
