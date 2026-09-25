using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Mate;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

[GameData]
public class MateGameData : Singleton<MateGameData>, IGameDataLoader
{
    private Dictionary<uint, NpcMountSkills> _npcMountSkills = [];
    private Dictionary<uint, MountSkills> _mountSkills = [];
    private Dictionary<uint, MountAttachedSkills> _mountAttachedSkills = [];
    private Dictionary<uint, MateEquipSlotPack> _mateEquipSlotPacks = [];
    private readonly MateSeatCatalog _seatCatalog = new();

    /// <summary>mate_equip_pack_groups keyed by the mate's npc: the packs it may wear.</summary>
    private readonly Dictionary<uint, List<uint>> _mateEquipPacks = [];

    /// <summary>mate_equip_pack_items keyed by pack: the items a pack holds.</summary>
    private readonly Dictionary<uint, HashSet<uint>> _mateEquipPackItems = [];

    /// <summary>
    /// Whether a mate's own pack lets it wear the given position. The pack is the npc's
    /// <c>mate_equip_slot_pack_id</c>; a mate whose npc points at no pack has nothing said about it, so
    /// the position is left to the item's own template.
    /// </summary>
    public bool MateWearsSlot(uint equipSlotPackId, MateEquipSlot slot)
    {
        return !_mateEquipSlotPacks.TryGetValue(equipSlotPackId, out var pack) || pack.AllowsSlot(slot);
    }

    /// <summary>
    /// Whether the packs of a mate's npc list an item. A mate whose npc carries no pack at all has no
    /// whitelist, so anything its positions take is allowed — the tables are silent, not restrictive.
    /// </summary>
    public bool MatePacksList(uint npcId, uint itemTemplateId)
    {
        if (!_mateEquipPacks.TryGetValue(npcId, out var packs) || packs.Count == 0)
            return true;

        foreach (var packId in packs)
        {
            if (_mateEquipPackItems.TryGetValue(packId, out var items) && items.Contains(itemTemplateId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves an npc's equip-slot pack to its mate type (enum_mate_types: 1 ride, 2 battle).
    /// </summary>
    public byte GetMateType(uint equipSlotPackId) =>
        _mateEquipSlotPacks.TryGetValue(equipSlotPackId, out var pack) ? pack.MateTypeId : (byte)0;

    /// <summary>
    /// Rider attach points explicitly joined to this NPC through mount skills. The catalog never
    /// infers seats from a capacity or equipment field.
    /// </summary>
    public IReadOnlyList<AttachPointKind> GetMateSeats(uint npcId) => _seatCatalog.GetSeats(npcId);

    /// <summary>
    /// Gets a list of pet skill Ids
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public List<uint> GetMateSkills(uint id)
    {
        var template = new List<uint>();

        foreach (var value in _npcMountSkills.Values)
            if (value.NpcId == id && !template.Contains(value.MountSkillId))
                template.Add(value.MountSkillId);

        return template;
    }

    /// <summary>
    /// True when <paramref name="skillId"/> is a <c>mount_skills.skill_id</c> granted to this NPC
    /// via <c>npc_mount_skills</c> (pet hotbar Scratch/Recover/etc.).
    /// </summary>
    public bool NpcHasMountSkill(uint npcId, uint skillId)
    {
        foreach (var npcMount in _npcMountSkills.Values)
        {
            if (npcMount.NpcId != npcId)
                continue;
            if (_mountSkills.TryGetValue(npcMount.MountSkillId, out var mountSkill) &&
                mountSkill.SkillId == skillId)
                return true;
        }

        return false;
    }

    /// <summary>
    /// <c>mount_skills.id</c> for a skill used as a mount/mate bar entry, or 0.
    /// </summary>
    public uint GetMountSkillIdBySkillId(uint skillId)
    {
        foreach (var ms in _mountSkills.Values)
        {
            if (ms.SkillId == skillId)
                return ms.Id;
        }

        return 0;
    }

    /// <summary>
    /// Get the associated rider skill for a given mountSkill
    /// </summary>
    /// <param name="mateSkill">The skill the mate used</param>
    /// <param name="attachPoint">The attachPoint the player is currently on</param>
    /// <returns></returns>
    public uint GetMountAttachedSkills(uint mateSkill, AttachPointKind attachPoint)
    {
        var id = 0u;
        var skill = 0u;

        // Find the mountSkillId for this mate's skill
        foreach (var ms in _mountSkills)
        {
            if (ms.Value.SkillId != mateSkill)
                continue;
            id = ms.Key;
            break;
        }

        if (id == 0)
            return 0;

        skill = FindAttachedSkill(id, attachPoint);
        // mount_attached_skills only defines Driver (and passenger) rows for sail fold/unfold.
        // Mast/Sail seats still show those skills on the client — fall back to Driver mapping.
        if (skill == 0 && IsMastOrSailAttachPoint(attachPoint))
            skill = FindAttachedSkill(id, AttachPointKind.Driver);

        return skill;
    }

    private uint FindAttachedSkill(uint mountSkillId, AttachPointKind attachPoint)
    {
        foreach (var mas in _mountAttachedSkills)
        {
            if (mas.Value.MountSkillId != mountSkillId || mas.Value.AttachPointId != attachPoint)
                continue;
            return mas.Value.SkillId;
        }

        return 0;
    }

    private static bool IsMastOrSailAttachPoint(AttachPointKind attachPoint) =>
        attachPoint is AttachPointKind.Mast0 or AttachPointKind.Mast1 or AttachPointKind.Mast2
            or AttachPointKind.Sail0 or AttachPointKind.Sail1 or AttachPointKind.Sail2;

    /// <summary>
    /// Gets MountSkillId for use with Slaves
    /// </summary>
    /// <param name="slaveSkillId"></param>
    /// <returns></returns>
    public uint GetMountSkillIdForSkill(uint slaveSkillId)
    {
        foreach (var ms in _mountSkills.Values)
        {
            if (ms.SkillId == slaveSkillId)
                return ms.Id;
        }

        return 0;
    }

    /// <summary>
    /// Loads the game db data for pets
    /// </summary>
    /// <param name="connection"></param>
    public void Load(SqliteConnection connection)
    {
        _npcMountSkills = [];
        _mountSkills = [];
        _mountAttachedSkills = [];
        _mateEquipSlotPacks = [];

        #region MateTables

        // Npc Mount skills
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM npc_mount_skills";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new NpcMountSkills
                    {
                        Id = reader.GetUInt32("id"),
                        NpcId = reader.GetUInt32("npc_id"),
                        MountSkillId = reader.GetUInt32("mount_skill_id")
                    };
                    _npcMountSkills.Add(template.Id, template);
                }
            }
        }

        // Mount Skills
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM mount_skills";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new MountSkills
                    {
                        Id = reader.GetUInt32("id"),
                        Name = reader.GetString("name", ""),
                        SkillId = reader.GetUInt32("skill_id")
                    };
                    _mountSkills.Add(template.Id, template);
                }
            }
        }

        // Mount attached skills
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM mount_attached_skills";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new MountAttachedSkills
                    {
                        Id = reader.GetUInt32("id"),
                        MountSkillId = reader.GetUInt32("mount_skill_id"),
                        AttachPointId = (AttachPointKind)reader.GetUInt32("attach_point_id"),
                        SkillId = reader.GetUInt32("skill_id")
                    };
                    _mountAttachedSkills.Add(template.Id, template);
                }
            }
        }

        // Mate equip slot packs — the npc's pack carries the mate type and which positions it may wear
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM mate_equip_slot_packs";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var template = new MateEquipSlotPack
                    {
                        Id = reader.GetUInt32("id"),
                        MateTypeId = (byte)reader.GetUInt32("mate_type_id", 0),
                        Head = reader.GetBoolean("head"),
                        Chest = reader.GetBoolean("chest"),
                        Waist = reader.GetBoolean("waist"),
                        Feet = reader.GetBoolean("feet")
                    };
                    _mateEquipSlotPacks.Add(template.Id, template);
                }
            }
        }

        // mate_equip_pack_groups: the packs a mate's npc may wear; mate_equip_pack_items: what a pack holds
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT npc_id, mate_equip_pack_id FROM mate_equip_pack_groups";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var npcId = reader.GetUInt32("npc_id");
                    var packId = reader.GetUInt32("mate_equip_pack_id");
                    if (!_mateEquipPacks.TryGetValue(npcId, out var packs))
                    {
                        packs = [];
                        _mateEquipPacks[npcId] = packs;
                    }

                    packs.Add(packId);
                }
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT mate_equip_pack_id, item_id FROM mate_equip_pack_items";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var packId = reader.GetUInt32("mate_equip_pack_id");
                    var itemId = reader.GetUInt32("item_id");
                    if (!_mateEquipPackItems.TryGetValue(packId, out var items))
                    {
                        items = [];
                        _mateEquipPackItems[packId] = items;
                    }

                    items.Add(itemId);
                }
            }
        }

        #endregion MateTables

        _seatCatalog.Load(connection);
    }

    public void PostLoad()
    {
        // Nothing to do here
    }
}
