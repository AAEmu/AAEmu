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

    /// <summary>
    /// Resolves an npc's equip-slot pack to its mate type (enum_mate_types: 1 ride, 2 battle).
    /// </summary>
    public byte GetMateType(uint equipSlotPackId) =>
        _mateEquipSlotPacks.TryGetValue(equipSlotPackId, out var pack) ? pack.MateTypeId : (byte)0;

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

        // Mate equip slot packs — the npc's pack carries the mate type
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
                        MateTypeId = (byte)reader.GetUInt32("mate_type_id", 0)
                    };
                    _mateEquipSlotPacks.Add(template.Id, template);
                }
            }
        }

        #endregion MateTables
    }

    public void PostLoad()
    {
        // Nothing to do here
    }
}
