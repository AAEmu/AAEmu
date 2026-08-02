using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Slaves;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData;

[GameData]
public class SlaveGameData : Singleton<SlaveGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private readonly Dictionary<uint, SlaveTemplate> _slaveTemplates = []; // TODO: Move to global/static
    private Dictionary<uint, Dictionary<AttachPointKind, WorldSpawnPosition>> _attachPoints = [];
    private readonly Dictionary<uint, List<SlaveInitialItems>> _slaveInitialItems = []; // PackId and List<Slot/ItemData>
    private readonly Dictionary<uint, SlaveMountSkills> _slaveMountSkills = [];

    /// <summary>item_slave_equipments keyed by item_id.</summary>
    private readonly Dictionary<uint, SlaveEquipVisual> _itemSlaveEquipments = [];

    /// <summary>item_slave_equipment_grade_spawns keyed by (item_id, item_grade_id).</summary>
    private readonly Dictionary<(uint itemId, byte grade), SlaveEquipVisual> _itemSlaveEquipmentGradeSpawns = [];

    /// <summary>slave_equip_slots: slaveTemplateId → equipSlotId → attach point.</summary>
    private readonly Dictionary<uint, Dictionary<byte, AttachPointKind>> _slaveEquipSlots = [];

    /// <summary>slave_collision_damages keyed by id.</summary>
    private readonly Dictionary<uint, SlaveCollisionDamageDesc> _slaveCollisionDamages = [];

    public void Load(SqliteConnection connection)
    {
        #region SQLLite

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM slaves";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new SlaveTemplate
                    {
                        Id = reader.GetUInt32("id"),
                        Name =
                            LocalizationManager.Instance.Get("slaves", "name", reader.GetUInt32("id"),
                                reader.GetString("name")),
                        ModelId = reader.GetUInt32("model_id"),
                        Mountable = reader.GetBoolean("mountable"),
                        SpawnXOffset = reader.GetFloat("spawn_x_offset"),
                        SpawnYOffset = reader.GetFloat("spawn_y_offset"),
                        FactionId = (FactionsEnum)reader.GetUInt32("faction_id", 0),
                        Level = reader.GetUInt32("level"),
                        Cost = reader.GetInt32("cost"),
                        SlaveKind = (SlaveKind)reader.GetUInt32("slave_kind_id"),
                        SpawnValidAreaRance = reader.GetUInt32("spawn_valid_area_range", 0),
                        SlaveInitialItemPackId = reader.GetUInt32("slave_initial_item_pack_id", 0),
                        SlaveCustomizingId = reader.GetUInt32("slave_customizing_id", 0),
                        Customizable = reader.GetBoolean("customizable", false),
                        PortalTime = reader.GetFloat("portal_time"),
                        PortalSpawnFxId = reader.GetUInt32("portal_spawn_fx_id", 0),
                        PortalDespawnFxId = reader.GetUInt32("portal_despawn_fx_id", 0),
                        PortalScale = reader.GetFloat("portal_scale", 1f),
                        Hp25DoodadCount = reader.GetInt32("hp25_doodad_count"),
                        Hp50DoodadCount = reader.GetInt32("hp50_doodad_count"),
                        Hp75DoodadCount = reader.GetInt32("hp75_doodad_count"),
                        SlaveCollisionDamageId = reader.GetUInt32("slave_collision_damage_id", 0),
                    };
                    _slaveTemplates.Add(template.Id, template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM unit_modifiers WHERE owner_type='Slave'";
            command.Prepare();
            using (var sqliteDataReader = command.ExecuteReader())
            using (var reader = new SQLiteWrapperReader(sqliteDataReader))
            {
                while (reader.Read())
                {
                    var slaveId = reader.GetUInt32("owner_id");
                    if (!_slaveTemplates.TryGetValue(slaveId, out var slaveTemplate))
                        continue;
                    var template = new BonusTemplate
                    {
                        Attribute = (UnitAttribute)reader.GetUInt32("unit_attribute_id", 0),
                        ModifierType = (UnitModifierType)reader.GetByte("unit_modifier_type_id"),
                        Value = reader.GetInt64("value"),
                        LinearLevelBonus = reader.GetInt32("linear_level_bonus")
                    };
                    slaveTemplate.Bonuses.Add(template);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM slave_collision_damages";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var desc = new SlaveCollisionDamageDesc
                    {
                        Id = reader.GetUInt32("id"),
                        FrontGain = reader.GetFloat("front_gain", 1f),
                        SideGain = reader.GetFloat("side_gain", 1f),
                        RearGain = reader.GetFloat("rear_gain", 1f),
                        BottomGain = reader.GetFloat("bottom_gain", 1f),
                        TopGain = reader.GetFloat("top_gain", 1f),
                        FrontLimit = reader.GetInt32("front_limit", 0),
                        SideLimit = reader.GetInt32("side_limit", 0),
                        RearLimit = reader.GetInt32("rear_limit", 0),
                        BottomLimit = reader.GetInt32("bottom_limit", 0),
                        TopLimit = reader.GetInt32("top_limit", 0),
                    };
                    _slaveCollisionDamages[desc.Id] = desc;
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM slave_initial_items";
            command.Prepare();

            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var itemPackId = reader.GetUInt32("slave_initial_item_pack_id");
                    var slotId = reader.GetByte("equip_slot_id");
                    var item = reader.GetUInt32("item_id");

                    if (_slaveInitialItems.TryGetValue(itemPackId, out var key))
                    {
                        key.Add(new SlaveInitialItems
                        {
                            slaveInitialItemPackId = itemPackId, equipSlotId = slotId, itemId = item
                        });
                    }
                    else
                    {
                        var newPack = new List<SlaveInitialItems>();
                        var newKey = new SlaveInitialItems
                        {
                            slaveInitialItemPackId = itemPackId, equipSlotId = slotId, itemId = item
                        };
                        newPack.Add(newKey);

                        _slaveInitialItems.Add(itemPackId, newPack);
                    }
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM slave_initial_buffs";
            command.Prepare();

            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new SlaveInitialBuffs
                    {
                        Id = reader.GetUInt32("id"),
                        SlaveId = reader.GetUInt32("slave_id"),
                        BuffId = reader.GetUInt32("buff_id")
                    };
                    if (_slaveTemplates.TryGetValue(template.SlaveId, out var value))
                    {
                        value.InitialBuffs.Add(template);
                    }
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM slave_passive_buffs";
            command.Prepare();

            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new SlavePassiveBuffs
                    {
                        Id = reader.GetUInt32("id"),
                        OwnerId = reader.GetUInt32("owner_id"),
                        OwnerType = reader.GetString("owner_type"),
                        PassiveBuffId = reader.GetUInt32("passive_buff_id")
                    };
                    if (_slaveTemplates.TryGetValue(template.OwnerId, out var value))
                    {
                        value.PassiveBuffs.Add(template);
                    }
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM slave_doodad_bindings";
            command.Prepare();

            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new SlaveDoodadBindings
                    {
                        Id = reader.GetUInt32("id"),
                        OwnerId = reader.GetUInt32("owner_id"),
                        OwnerType = reader.GetString("owner_type"),
                        AttachPointId = (AttachPointKind)reader.GetInt32("attach_point_id"),
                        DoodadId = reader.GetUInt32("doodad_id"),
                        Persist = reader.GetBoolean("persist", true),
                        Scale = reader.GetFloat("scale")
                    };
                    if (_slaveTemplates.TryGetValue(template.OwnerId, out var value))
                    {
                        value.DoodadBindings.Add(template);
                    }
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM slave_healing_point_doodads";
            command.Prepare();

            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new SlaveDoodadBindings
                    {
                        Id = reader.GetUInt32("id"),
                        OwnerId = reader.GetUInt32("owner_id"),
                        OwnerType = reader.GetString("owner_type"),
                        AttachPointId = (AttachPointKind)reader.GetInt32("attach_point_id"),
                        DoodadId = reader.GetUInt32("doodad_id"),
                        Persist = false,
                        Scale = 1f
                    };
                    if (_slaveTemplates.TryGetValue(template.OwnerId, out var value))
                    {
                        value.HealingPointDoodads.Add(template);
                    }
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM slave_bindings";
            command.Prepare();

            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new SlaveBindings
                    {
                        Id = reader.GetUInt32("id"),
                        OwnerId = reader.GetUInt32("owner_id"),
                        OwnerType = reader.GetString("owner_type"),
                        AttachPointId = (AttachPointKind)reader.GetUInt32("attach_point_id"),
                        SlaveId = reader.GetUInt32("slave_id")
                    };

                    if (_slaveTemplates.TryGetValue(template.OwnerId, out var value))
                    {
                        value.SlaveBindings.Add(template);
                    }
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM slave_drop_doodads";
            command.Prepare();

            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new SlaveDropDoodad
                    {
                        Id = reader.GetUInt32("id"),
                        OwnerId = reader.GetUInt32("owner_id"),
                        OwnerType = reader.GetString("owner_type"),
                        DoodadId = reader.GetUInt32("doodad_id"),
                        Count = reader.GetUInt32("count"),
                        Radius = reader.GetFloat("radius"),
                        OnWater = reader.GetBoolean("on_water", true),
                    };

                    if (template.OwnerType != "Slave")
                    {
                        Logger.Warn($"Non slave-owned drops defined in slave_drop_doodads table");
                        continue;
                    }

                    if (_slaveTemplates.TryGetValue(template.OwnerId, out var value))
                    {
                        value.SlaveDropDoodads.Add(template);
                    }
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM slave_mount_skills";
            command.Prepare();

            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new SlaveMountSkills
                    {
                        Id = reader.GetUInt32("id"),
                        SlaveId = reader.GetUInt32("slave_id"),
                        MountSkillId = reader.GetUInt32("mount_skill_id")
                    };

                    if (!_slaveMountSkills.TryAdd(template.Id, template))
                        Logger.Warn($"Duplicate entry for slave_mount_skills");
                }
            }
        }

        // 10.0.2.13: the 'repairable_slaves' table (slave_id -> repair_slave_effect_id mapping) was removed.
        // Repair amounts now come solely from 'repair_slave_effects' (loaded as RepairSlaveEffect templates in
        // SkillManager); there is no longer a per-slave repair-effect gate.

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT item_id, slave_id, doodad_id, doodad_scale FROM item_slave_equipments";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var itemId = reader.GetUInt32("item_id");
                    var visual = new SlaveEquipVisual(
                        reader.GetUInt32("slave_id", 0),
                        reader.GetUInt32("doodad_id", 0),
                        reader.GetFloat("doodad_scale", 1f));
                    if (visual.IsEmpty)
                        continue;
                    if (!_itemSlaveEquipments.TryAdd(itemId, visual))
                        Logger.Warn("Duplicate item_slave_equipments row for item_id={0}", itemId);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT item_id, item_grade_id, slave_id, doodad_id FROM item_slave_equipment_grade_spawns";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var key = (reader.GetUInt32("item_id"), reader.GetByte("item_grade_id"));
                    var visual = new SlaveEquipVisual(
                        reader.GetUInt32("slave_id", 0),
                        reader.GetUInt32("doodad_id", 0),
                        1f);
                    if (visual.IsEmpty)
                        continue;
                    if (!_itemSlaveEquipmentGradeSpawns.TryAdd(key, visual))
                        Logger.Warn("Duplicate item_slave_equipment_grade_spawns for item={0} grade={1}", key.Item1, key.Item2);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT slave_id, equip_slot_id, attach_point_id FROM slave_equip_slots";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var slaveId = reader.GetUInt32("slave_id");
                    var equipSlotId = reader.GetByte("equip_slot_id");
                    var attachPoint = (AttachPointKind)reader.GetByte("attach_point_id");
                    if (!_slaveEquipSlots.TryGetValue(slaveId, out var slots))
                    {
                        slots = new Dictionary<byte, AttachPointKind>();
                        _slaveEquipSlots[slaveId] = slots;
                    }

                    slots[equipSlotId] = attachPoint;
                }
            }
        }

        Logger.Info(
            "Slave equip visuals: {0} item maps, {1} grade maps, {2} slave slot tables",
            _itemSlaveEquipments.Count,
            _itemSlaveEquipmentGradeSpawns.Count,
            _slaveEquipSlots.Count);
        #endregion

        LoadSlaveAttachmentPointLocations();
    }

    public void PostLoad()
    {
        //
    }
    
    
    public bool Exist(uint templateId)
    {
        return _slaveTemplates.ContainsKey(templateId);
    }

    public SlaveTemplate GetSlaveTemplate(uint id)
    {
        return _slaveTemplates.GetValueOrDefault(id);
    }

    /// <summary>Per-face collision gains/limits for a hull, or null when the slave defines none.</summary>
    public SlaveCollisionDamageDesc GetCollisionDamageDesc(uint slaveCollisionDamageId)
    {
        return _slaveCollisionDamages.GetValueOrDefault(slaveCollisionDamageId);
    }

    /// <summary>
    /// Loads attachment points from slave_attach_points*.json files
    /// </summary>
    /// <exception cref="IOException"></exception>
    public void LoadSlaveAttachmentPointLocations()
    {
        Logger.Info("Loading Slave Model Attach Points...");

        // Get all files matching the pattern in the Data folder.
        var dataFolder = Path.Combine(FileManager.AppPath, "Data");
        string[] attachFiles;
        try
        {
            attachFiles = Directory.GetFiles(dataFolder, "slave_attach_points*.json");
        }
        catch (Exception ex)
        {
            Logger.Error($"Error retrieving slave attachment point files: {ex.Message}");
            return;
        }

        var allAttachPoints = new List<SlaveModelAttachPoint>();

        foreach (var filePath in attachFiles)
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

            if (JsonHelper.TryDeserializeObject(contents, out List<SlaveModelAttachPoint> attachPoints, out _))
            {
                allAttachPoints.AddRange(attachPoints);
            }
            else
            {
                Logger.Error($"Error parsing {filePath}");
                // continue;
            }
        }

        // Log the count with proper singular/plural grammar.
        var count = allAttachPoints.Count;
        Logger.Info($"{count} slave model attach point{(count == 1 ? "" : "s")} loaded...");

        // Convert degrees from JSON to radians.
        foreach (var vehicle in allAttachPoints)
        {
            foreach (var pos in vehicle.AttachPoints)
            {
                pos.Value.Roll = pos.Value.Roll.DegToRad();
                pos.Value.Pitch = pos.Value.Pitch.DegToRad();
                pos.Value.Yaw = pos.Value.Yaw.DegToRad();
            }
        }

        _attachPoints = [];
        foreach (var set in allAttachPoints)
        {
            _attachPoints[set.ModelId] = set.AttachPoints;
        }
    }

    /// <summary>
    /// Get mount skill associated with slaveMountSkillId
    /// </summary>
    /// <param name="slaveMountSkillId"></param>
    /// <returns></returns>
    public uint GetSlaveMountSkillFromId(uint slaveMountSkillId)
    {
        return _slaveMountSkills.TryGetValue(slaveMountSkillId, out var res) ? res.MountSkillId : 0;
    }

    /// <summary>
    /// Gets a list of all mount skills for a given slave type
    /// </summary>
    /// <param name="slaveTemplateId"></param>
    /// <returns></returns>
    public List<uint> GetSlaveMountSkillList(uint slaveTemplateId)
    {
        var res = new List<uint>();
        foreach (var q in _slaveMountSkills.Values.Where(q => q.SlaveId == slaveTemplateId))
            res.Add(q.MountSkillId);
        return res;
    }

    public List<SlaveInitialItems> GetSlaveInitialItemPack(uint templateId)
    {
        return _slaveInitialItems.GetValueOrDefault(templateId);
    }

    /// <summary>
    /// Attach point for a slave equipment slot (slave_equip_slots), or null if that slave/slot has none.
    /// </summary>
    public bool TryGetEquipAttachPoint(uint slaveTemplateId, byte equipSlotId, out AttachPointKind attachPoint)
    {
        attachPoint = AttachPointKind.None;
        if (!_slaveEquipSlots.TryGetValue(slaveTemplateId, out var slots))
            return false;
        return slots.TryGetValue(equipSlotId, out attachPoint);
    }

    /// <summary>
    /// Prefer grade-specific spawn, else base item_slave_equipments row.
    /// </summary>
    public bool TryResolveEquipVisual(uint itemId, byte grade, out SlaveEquipVisual visual)
    {
        if (_itemSlaveEquipmentGradeSpawns.TryGetValue((itemId, grade), out visual))
            return !visual.IsEmpty;
        if (_itemSlaveEquipments.TryGetValue(itemId, out visual))
            return !visual.IsEmpty;
        visual = default;
        return false;
    }

    /// <summary>
    /// True when this attach point is claimed by static doodad/slave bindings (not equipment-driven).
    /// </summary>
    public static bool IsBindingAttachPoint(SlaveTemplate template, AttachPointKind attachPoint)
    {
        if (template == null)
            return false;
        if (template.DoodadBindings.Any(b => b.AttachPointId == attachPoint))
            return true;
        if (template.SlaveBindings.Any(b => b.AttachPointId == attachPoint))
            return true;
        if (template.HealingPointDoodads.Any(b => b.AttachPointId == attachPoint))
            return true;
        return false;
    }

    /// <summary>
    /// Attach points for a slave model. The client's own meshes are the source of record — the json table is
    /// only consulted for models the pak could not be read for, and for the handful it covers that the meshes
    /// do not.
    /// </summary>
    public Dictionary<AttachPointKind, WorldSpawnPosition> GetAttachPointsForSlave(uint modelId)
    {
        var fromClient = ModelAttachPointGameData.Instance.GetAttachPoints(modelId);
        var fromJson = _attachPoints.GetValueOrDefault(modelId);

        if (fromClient == null)
            return fromJson;
        if (fromJson == null)
            return fromClient;

        // The json wins where it has a value. Its offsets are what the live server has been running on, and
        // the two disagree on a handful of points for reasons not yet run down; the client data is here to
        // fill the gaps, not to relitigate entries that already work.
        var merged = new Dictionary<AttachPointKind, WorldSpawnPosition>(fromClient);
        foreach (var (attachPoint, position) in fromJson)
            merged[attachPoint] = position;
        return merged;
    }

}
