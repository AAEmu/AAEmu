using AAEmu.Commons.IO;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData;

[GameData]
public class HousingGameData : Singleton<HousingGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, HousingDecoration> _housingDecorations = [];
    private List<ItemHousingDecoration> _housingItemHousingDecorations = [];
    private List<HousingItemHousings> _housingItemHousings = [];
    private Dictionary<uint, HousingTemplate> _housingTemplates = [];

    public void Load(SqliteConnection connection)
    {
        _housingTemplates = [];
        _housingItemHousings = [];
        _housingDecorations = [];
        _housingItemHousingDecorations = [];

        // var houseTaxes = new Dictionary<uint, HouseTax>();

        Logger.Info("Loading Housing Information ...");

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM housing_areas WHERE activated = 't'";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var zoneName = reader.GetString("name", string.Empty);
                    if (string.IsNullOrEmpty(zoneName))
                        continue;
                    if (!_zoneHousingGroups.TryGetValue(zoneName, out var groups))
                        _zoneHousingGroups[zoneName] = groups = [];
                    groups.Add(reader.GetUInt32("housing_group_id", 0));
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM housing_group_categories";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var group = reader.GetUInt32("housing_group_id", 0);
                    if (!_groupCategories.TryGetValue(group, out var categories))
                        _groupCategories[group] = categories = [];
                    categories.Add(reader.GetUInt32("category_id", 0));
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_housings";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new HousingItemHousings
                    {
                        Id = reader.GetUInt32("id"),
                        Item_Id = reader.GetUInt32("item_id"),
                        Design_Id = reader.GetUInt32("design_id")
                    };
                    _housingItemHousings.Add(template);
                }
            }
        }

        Logger.Info("Loading Housing Templates...");
        // Define the folder path where your housing binding files reside.
        var dataFolder = Path.Combine(FileManager.AppPath, "Data");

        // Call the multi-file loader function.
        var binding = LoadHousingBindings(dataFolder);

        // Log the outcome based on whether bindings were found.
        if (binding.Count > 0)
        {
            Logger.Info($"{binding.Count} housing binding{(binding.Count == 1 ? "" : "s")} loaded...");
        }
        else
        {
            Logger.Warn("Housing bindings not loaded...");
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM housings";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new HousingTemplate
                    {
                        Id = reader.GetUInt32("id"),
                        Name = reader.GetString("name"),
                        CategoryId = reader.GetUInt32("category_id"),
                        MainModelId = reader.GetUInt32("main_model_id"),
                        DoorModelId = reader.GetUInt32("door_model_id", 0),
                        StairModelId = reader.GetUInt32("stair_model_id", 0),
                        AutoZ = reader.GetBoolean("auto_z", true),
                        GateExists = reader.GetBoolean("gate_exists", true),
                        Hp = reader.GetInt32("hp"),
                        RepairCost = reader.GetUInt32("repair_cost"),
                        // 10.0.2.13: garden_radius removed
                        Family = reader.GetString("family"),
                        TaxationId = reader.GetUInt32("taxation_id"),
                        GuardTowerSettingId = reader.GetUInt32("guard_tower_setting_id", 0),
                        CinemaRadius = reader.GetFloat("cinema_radius"),
                        AutoZOffsetX = reader.GetFloat("auto_z_offset_x"),
                        AutoZOffsetY = reader.GetFloat("auto_z_offset_y"),
                        AutoZOffsetZ = reader.GetFloat("auto_z_offset_z"),
                        Alley = reader.GetFloat("alley"),
                        ExtraHeightAbove = reader.GetFloat("extra_height_above"),
                        ExtraHeightBelow = reader.GetFloat("extra_height_below"),
                        DecoLimit = reader.GetUInt32("deco_limit"),
                        AbsoluteDecoLimit = reader.GetUInt32("absolute_deco_limit"),
                        HousingDecoLimitId = reader.GetUInt32("housing_deco_limit_id", 0),
                        IsSellable = reader.GetBoolean("is_sellable", true),
                        HeavyTax = reader.GetBoolean("heavy_tax", true),
                        AlwaysPublic = reader.GetBoolean("always_public", true)
                    };
                    _housingTemplates.Add(template.Id, template);

                    var templateBindings = binding.Find(x => x.TemplateId.Contains(template.Id));
                    using (var command2 = connection.CreateCommand())
                    {
                        command2.CommandText = "SELECT * FROM housing_binding_doodads WHERE housing_id=@housing_id";
                        command2.Parameters.AddWithValue("housing_id", template.Id);
                        command2.Prepare();
                        using (var reader2 = new SQLiteWrapperReader(command2.ExecuteReader()))
                        {
                            var doodads = new List<HousingBindingDoodad>();
                            while (reader2.Read())
                            {
                                var bindingDoodad = new HousingBindingDoodad
                                {
                                    AttachPointId = (AttachPointKind)reader2.GetInt16("attach_point_id"),
                                    DoodadId = reader2.GetUInt32("doodad_id")
                                };

                                if (templateBindings != null &&
                                    templateBindings.AttachPointId.TryGetValue(bindingDoodad.AttachPointId,
                                        out var pos))
                                    bindingDoodad.Position = pos.Clone();

                                bindingDoodad.Position ??= new WorldSpawnPosition();

                                doodads.Add(bindingDoodad);
                            }

                            template.HousingBindingDoodad = doodads.ToArray();
                        }
                    }
                }
            }
        }

        Logger.Info($"Loaded Housing Templates {_housingTemplates.Count}");
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM housing_build_steps";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var housingId = reader.GetUInt32("housing_id");
                    if (!_housingTemplates.ContainsKey(housingId))
                        continue;

                    var template = new HousingBuildStep
                    {
                        Id = reader.GetUInt32("id"),
                        HousingId = housingId,
                        Step = reader.GetInt16("step"),
                        ModelId = reader.GetUInt32("model_id"),
                        SkillId = reader.GetUInt32("skill_id"),
                        NumActions = reader.GetInt32("num_actions")
                    };

                    _housingTemplates[housingId].BuildSteps.Add(template.Step, template);
                }
            }
        }

        Logger.Info("Loaded Decoration Templates...");
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM housing_decorations";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new HousingDecoration
                    {
                        Id = reader.GetUInt32("id"),
                        Name = reader.GetString("name"),
                        AllowOnFloor = reader.GetBoolean("allow_on_floor"),
                        AllowOnWall = reader.GetBoolean("allow_on_wall"),
                        AllowOnCeiling = reader.GetBoolean("allow_on_ceiling"),
                        DoodadId = reader.GetUInt32("doodad_id"),
                        // 10.0.2.13: allow_pivot_on_garden removed
                        ActabilityGroupId =
                            !reader.IsDBNull("actability_group_id") ? reader.GetUInt32("actability_group_id") : 0,
                        ActabilityUp = !reader.IsDBNull("actability_up") ? reader.GetUInt32("actability_up") : 0,
                        DecoActAbilityGroupId =
                            !reader.IsDBNull("deco_actability_group_id")
                                ? reader.GetUInt32("deco_actability_group_id")
                                : 0
                        // 10.0.2.13: allow_mesh_on_garden removed
                    };

                    _housingDecorations.Add(template.Id, template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM item_housing_decorations";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new ItemHousingDecoration
                    {
                        Id = reader.GetUInt32("id"),
                        ItemId = reader.GetUInt32("item_id"),
                        DesignId = reader.GetUInt32("design_id"),
                        Restore = reader.GetBoolean("restore", true)
                    };
                    _housingItemHousingDecorations.Add(template);
                }
            }
        }

    }

    public void PostLoad()
    {
        foreach (var (_, template) in _housingTemplates)
        {
            template.Name = LocalizationManager.Instance.Get("housings", "name", template.Id, template.Name);
            template.Taxation = TaxationsManager.Instance.taxations.GetValueOrDefault(template.TaxationId);
        }

        ResolveBindingPositionsFromClientData();
    }

    /// <summary>
    /// Replaces the binding offsets taken from housing_bindings.json with the ones held in the model the house
    /// actually uses. Runs in PostLoad because the attach point table is built by another loader and the
    /// loaders' Load() order is reflection order.
    ///
    /// The json only ever covered 104 of the 631 templates that have bindings, keyed by template id; everything
    /// else fell back to (0,0,0) and stacked its doodads on the house origin. Attach point geometry belongs to
    /// the model, not the template, so templates sharing a model now resolve from the same place.
    /// </summary>
    private void ResolveBindingPositionsFromClientData()
    {
        if (!ModelAttachPointGameData.Instance.HasData)
            return;

        var resolved = 0;
        var unresolved = 0;

        foreach (var (_, template) in _housingTemplates)
        {
            if (template.HousingBindingDoodad == null)
                continue;

            foreach (var bindingDoodad in template.HousingBindingDoodad)
            {
                // Only fill the gaps. Where housing_bindings.json has an offset it stays — the two sources
                // disagree on a handful of points and the json is what the server has been running on.
                if (bindingDoodad.Position != null && bindingDoodad.Position.AsPositionVector() != Vector3.Zero)
                    continue;

                var pos = ModelAttachPointGameData.Instance
                    .GetAttachPoint(template.MainModelId, bindingDoodad.AttachPointId);

                if (pos != null)
                {
                    bindingDoodad.Position = pos.Clone();
                    resolved++;
                }
                else
                {
                    unresolved++;
                }
            }
        }

        Logger.Info($"Housing binding offsets: {resolved} from client models, {unresolved} still without a position");
    }

    private List<HousingBindingTemplate> LoadHousingBindings(string dataFolder)
    {
        Logger.Info("Loading Housing Templates...");
        var housingBindings = new List<HousingBindingTemplate>();
        string[] bindingFiles;

        try
        {
            bindingFiles = Directory.GetFiles(dataFolder, "housing_bindings*.json");
        }
        catch (Exception ex)
        {
            Logger.Error($"Error retrieving housing binding files: {ex.Message}");
            return housingBindings;
        }

        foreach (var filePath in bindingFiles)
        {
            if (!File.Exists(filePath))
            {
                Logger.Info($"Missing file: {Path.GetFileName(filePath)}");
                continue;
            }

            var contents = FileManager.GetFileContents(filePath);
            if (string.IsNullOrWhiteSpace(contents))
            {
                Logger.Warn($"File {filePath} is empty.");
                continue;
            }

            if (JsonHelper.TryDeserializeObject(contents, out List<HousingBindingTemplate> templates, out _))
            {
                housingBindings.AddRange(templates);
            }
            else
            {
                Logger.Error($"Error parsing {filePath}");
                // Simply log and continue; no exception thrown.
                continue;
            }
        }

        return housingBindings;
    }

    /// <summary>
    /// Gets a template by it's design Id
    /// </summary>
    /// <param name="designId"></param>
    /// <returns></returns>
    /// <summary>zone name -> housing groups whose areas are activated there (housing_areas).</summary>
    private readonly Dictionary<string, HashSet<uint>> _zoneHousingGroups = [];

    /// <summary>housing group -> house categories it permits (housing_group_categories).</summary>
    private readonly Dictionary<uint, HashSet<uint>> _groupCategories = [];

    /// <summary>
    /// True when a house of <paramref name="categoryId"/> may be built in the named zone.
    /// </summary>
    /// <remarks>
    /// housing_areas names the zone a group's areas sit in, and housing_group_categories says which
    /// house categories each group permits, so a zone with no activated area rejects everything and a
    /// zone that only hosts farm groups rejects a manor. This is the coarse half of the rule: the
    /// exact buildable outlines are LevelDesignShape objects in packed level data, which only the
    /// zone loads, so a position inside the right zone but outside a shape still passes here.
    ///
    /// going away. Nothing in the zone judges a placement, so a shape-accurate test has to read the
    /// LevelDesignShape geometry rather than wait for the zone to object.
    /// </remarks>
    public bool IsCategoryAllowedInZone(string zoneName, uint categoryId)
    {
        if (string.IsNullOrEmpty(zoneName) || !_zoneHousingGroups.TryGetValue(zoneName, out var groups))
            return false;

        foreach (var group in groups)
            if (_groupCategories.TryGetValue(group, out var categories) && categories.Contains(categoryId))
                return true;

        return false;
    }

    public HousingTemplate GetTemplate(uint designId)
    {
        return _housingTemplates.GetValueOrDefault(designId);
    }

    /// <summary>
    /// Gets data for the item for a housing decoration
    /// </summary>
    /// <param name="decoDesignId"></param>
    /// <returns></returns>
    public ItemHousingDecoration GetItemHousingDecorations(uint decoDesignId)
    {
        return _housingItemHousingDecorations.Find(x => x.DesignId == decoDesignId);
    }

    /// <summary>
    /// Get original item template based on house design
    /// </summary>
    /// <param name="designId"></param>
    /// <returns></returns>
    public uint GetItemIdByDesign(uint designId)
    {
        var designs = _housingItemHousings.Where(h => h.Design_Id == designId);
        foreach (var design in designs)
        {
            if (ItemManager.Instance.GetTemplate(design.Item_Id) != null)
                return design.Item_Id;
        }
        return 0;
    }

    /// <summary>
    /// Get decoration design by Id
    /// </summary>
    /// <param name="designId"></param>
    /// <returns></returns>
    public HousingDecoration GetDecorationDesignFromId(uint designId)
    {
        return _housingDecorations.GetValueOrDefault(designId);
    }

    /// <summary>
    /// Get decoration design from it's doodad counterpart
    /// </summary>
    /// <param name="doodadId"></param>
    /// <returns></returns>
    public HousingDecoration GetDecorationDesignFromDoodadId(uint doodadId)
    {
        return _housingDecorations.FirstOrDefault(x => x.Value.DoodadId == doodadId).Value;
    }
}
