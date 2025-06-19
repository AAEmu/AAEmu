using AAEmu.Commons.IO;
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

        // var housingAreas = new Dictionary<uint, HousingAreas>();
        // var houseTaxes = new Dictionary<uint, HouseTax>();


        Logger.Info("Loading Housing Information ...");

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
                    var template = new HousingTemplate();
                    template.Id = reader.GetUInt32("id");
                    template.Name = LocalizationManager.Instance.Get("housings", "name", template.Id, reader.GetString("name"));
                    template.CategoryId = reader.GetUInt32("category_id");
                    template.MainModelId = reader.GetUInt32("main_model_id");
                    template.DoorModelId = reader.GetUInt32("door_model_id", 0);
                    template.StairModelId = reader.GetUInt32("stair_model_id", 0);
                    template.AutoZ = reader.GetBoolean("auto_z", true);
                    template.GateExists = reader.GetBoolean("gate_exists", true);
                    template.Hp = reader.GetInt32("hp");
                    template.RepairCost = reader.GetUInt32("repair_cost");
                    template.GardenRadius = reader.GetFloat("garden_radius");
                    template.Family = reader.GetString("family");
                    var taxationId = reader.GetUInt32("taxation_id");
                    template.Taxation = TaxationsManager.Instance.taxations.GetValueOrDefault(taxationId);
                    template.GuardTowerSettingId = reader.GetUInt32("guard_tower_setting_id", 0);
                    template.CinemaRadius = reader.GetFloat("cinema_radius");
                    template.AutoZOffsetX = reader.GetFloat("auto_z_offset_x");
                    template.AutoZOffsetY = reader.GetFloat("auto_z_offset_y");
                    template.AutoZOffsetZ = reader.GetFloat("auto_z_offset_z");
                    template.Alley = reader.GetFloat("alley");
                    template.ExtraHeightAbove = reader.GetFloat("extra_height_above");
                    template.ExtraHeightBelow = reader.GetFloat("extra_height_below");
                    template.DecoLimit = reader.GetUInt32("deco_limit");
                    template.AbsoluteDecoLimit = reader.GetUInt32("absolute_deco_limit");
                    template.HousingDecoLimitId = reader.GetUInt32("housing_deco_limit_id", 0);
                    template.IsSellable = reader.GetBoolean("is_sellable", true);
                    template.HeavyTax = reader.GetBoolean("heavy_tax", true);
                    template.AlwaysPublic = reader.GetBoolean("always_public", true);
                    _housingTemplates.Add(template.Id, template);

                    var templateBindings = binding.Find(x => x.TemplateId.Contains(template.Id));
                    using (var command2 = connection.CreateCommand())
                    {
                        command2.CommandText = "SELECT * FROM housing_binding_doodads WHERE owner_id=@owner_id AND owner_type='Housing'";
                        command2.Parameters.AddWithValue("owner_id", template.Id);
                        command2.Prepare();
                        using (var reader2 = new SQLiteWrapperReader(command2.ExecuteReader()))
                        {
                            var doodads = new List<HousingBindingDoodad>();
                            while (reader2.Read())
                            {
                                var bindingDoodad = new HousingBindingDoodad();
                                bindingDoodad.AttachPointId = (AttachPointKind)reader2.GetInt16("attach_point_id");
                                bindingDoodad.DoodadId = reader2.GetUInt32("doodad_id");

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
                        AllowOnFloor = reader.GetBoolean("allow_on_floor", true),
                        AllowOnWall = reader.GetBoolean("allow_on_wall", true),
                        AllowOnCeiling = reader.GetBoolean("allow_on_ceiling", true),
                        DoodadId = reader.GetUInt32("doodad_id"),
                        AllowPivotOnGarden = reader.GetBoolean("allow_pivot_on_garden", true),
                        ActabilityGroupId =
                            !reader.IsDBNull("actability_group_id") ? reader.GetUInt32("actability_group_id") : 0,
                        ActabilityUp = !reader.IsDBNull("actability_up") ? reader.GetUInt32("actability_up") : 0,
                        DecoActAbilityGroupId =
                            !reader.IsDBNull("deco_actability_group_id")
                                ? reader.GetUInt32("deco_actability_group_id")
                                : 0,
                        AllowMeshOnGarden = reader.GetBoolean("allow_mesh_on_garden", true)
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
        //
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
