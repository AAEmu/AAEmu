using System;
using System.Collections.Generic;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units.Static;

using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Char;

public class CharacterMates
{
    /*
     * TODO:
     * EQUIPMENT CHANGE
     * FINISH ATTRIBUTES
     * NAME FROM LOCALIZED TABLE
     */

    public Character Owner { get; set; }

    private readonly Dictionary<ulong, MateDb> _mates; // itemId, MountDb
    private readonly List<uint> _removedMates;

    public CharacterMates(Character owner)
    {
        Owner = owner;
        _mates = new Dictionary<ulong, MateDb>();
        _removedMates = new List<uint>();
    }

    public MateDb GetMateDbInfo(ulong itemId)
    {
        return _mates.TryGetValue(itemId, out var mate) ? mate : null;
    }

    private MateDb CreateNewMate(ulong itemId, NpcTemplate npctemplate)
    {
        if (_mates.ContainsKey(itemId)) return null;
        var template = new MateDb
        {
            // TODO
            Id = MateIdManager.Instance.GetNextId(),
            ItemId = itemId,
            Level = npctemplate.Level,
            Name = LocalizationManager.Instance.Get("npcs", "name", npctemplate.Id, npctemplate.Name), // npctemplate.Name,
            Owner = Owner.Id,
            Mileage = 0,
            Xp = ExperienceManager.Instance.GetExpForLevel(npctemplate.Level, true),
            Hp = 9999,
            Mp = 9999,
            UpdatedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _mates.Add(template.ItemId, template);
        return template;
    }

    public void SpawnMount(SkillItem skillData)
    {
        var item = Owner.Inventory.GetItemById(skillData.ItemId);
        if (item == null) return;

        var itemTemplate = (SummonMateTemplate)ItemManager.Instance.GetTemplate(item.TemplateId);
        var npcId = itemTemplate.NpcId;
        var template = NpcManager.Instance.GetTemplate(npcId);
        var tlId = (ushort)TlIdManager.Instance.GetNextId();
        var objId = ObjectIdManager.Instance.GetNextId();

        // check if there already is such an object or if there is an object of this type
        var oldMates = MateManager.Instance.GetActiveMates(Owner.ObjId);
        if (oldMates != null)
        {
            foreach (var oldMate in oldMates)
            {
                var mateDb = GetMateDbInfo(skillData.ItemId);
                if (mateDb != null && oldMate.DbInfo.Id == mateDb.Id)
                {
                    DespawnMate(oldMate); // such an object already exists
                    return;
                }

                if (oldMate.MateType == GetMateType(item))
                    DespawnMate(oldMate); // there is an object of this type
            }
        }

        var mateDbInfo = GetMateDbInfo(skillData.ItemId) ?? CreateNewMate(skillData.ItemId, template);

        var mount = new Units.Mate();
        mount.ObjId = objId;
        mount.TlId = tlId;
        mount.OwnerId = Owner.Id;
        mount.Name = mateDbInfo.Name;
        mount.TemplateId = template.Id;
        mount.Template = template;
        mount.ModelId = template.ModelId;
        mount.Faction = Owner.Faction;
        mount.Level = (byte)mateDbInfo.Level;
        mount.Hp = mateDbInfo.Hp > 0 ? mateDbInfo.Hp : 100;
        mount.Mp = mateDbInfo.Mp > 0 ? mateDbInfo.Mp : 100;
        mount.OwnerObjId = Owner.ObjId;
        mount.Id = mateDbInfo.Id;
        mount.ItemId = mateDbInfo.ItemId;
        mount.UserState = 1; // TODO
        mount.Experience = mateDbInfo.Xp;
        mount.Mileage = mateDbInfo.Mileage;
        mount.SpawnDelayTime = 0; // TODO
        mount.DbInfo = mateDbInfo;
        SetMateType(item, mount);

        mount.Transform = Owner.Transform.CloneDetached(mount);

        //foreach (var skill in MateManager.Instance.GetMateSkills(npcId))
        //    mount.Skills.Add(skill);
        var mateSkills = MateManager.Instance.GetMateSkills(npcId);
        if (mateSkills is { Count: > 0 })
            mount.Skills.AddRange(mateSkills);

        foreach (var buffId in template.Buffs)
        {
            var buff = SkillManager.Instance.GetBuffTemplate(buffId);
            if (buff == null)
                continue;

            var obj = new SkillCasterUnit(mount.ObjId);
            buff.Apply(mount, obj, mount, null, null, new EffectSource(), null, DateTime.UtcNow);
        }

        mount.Equipment = ItemManager.Instance.GetItemContainerForCharacter(Owner.Id, SlotType.PetRideEquipment, mount, mount.Id);
        mount.UpdateGearBonuses(null, null);

        // Cap stats to their max
        mount.Hp = Math.Min(mount.Hp, mount.MaxHp);
        mount.Mp = Math.Min(mount.Mp, mount.MaxMp);

        mount.Transform.Local.AddDistanceToFront(3f);
        //Logger.Warn($"Spawn the pet:{mount.ObjId} X={mount.Transform.World.Position.X} Y={mount.Transform.World.Position.Y}");
        MateManager.Instance.AddActiveMateAndSpawn(Owner, mount, item);
        mount.PostUpdateCurrentHp(mount, 0, mount.Hp, KillReason.Unknown);
    }

    private static MateType GetMateType(Item item)
    {
        MateType res;
        switch (item.Template.CategoryId)
        {
            case 92:
            case 109:
            case 176:
                res = MateType.Ride;
                break;
            case 95:
            case 191:
            default:
                res = MateType.Battle;
                break;
        }

        return res;
    }

    private static void SetMateType(Item item, Units.Mate mount)
    {
        switch (item.Template.CategoryId)
        {
            case 92:
            case 109:
            case 176:
                mount.MateType = MateType.Ride;
                break;
            case 95:
            case 191:
            default:
                mount.MateType = MateType.Battle;
                break;
        }
    }

    public void DespawnMate(uint tlId)
    {
        var mateInfo = MateManager.Instance.GetActiveMateByTlId(Owner.ObjId, tlId);
        if (mateInfo != null)
        {
            var mateDbInfo = GetMateDbInfo(mateInfo.ItemId);
            if (mateDbInfo != null)
            {
                mateDbInfo.Hp = mateInfo.Hp;
                mateDbInfo.Mp = mateInfo.Mp;
                mateDbInfo.Level = mateInfo.Level;
                mateDbInfo.Xp = mateInfo.Experience;
                mateDbInfo.Mileage = mateInfo.Mileage;
                mateDbInfo.Name = mateInfo.Name;
                mateDbInfo.UpdatedAt = DateTime.UtcNow;
            }
        }

        MateManager.Instance.RemoveActiveMateAndDespawn(Owner, mateInfo);
    }

    public void DespawnMate(Units.Mate mateInfo)
    {
        if (mateInfo == null) { return; }
        var mateDbInfo = GetMateDbInfo(mateInfo.ItemId);
        if (mateDbInfo != null)
        {
            mateDbInfo.Hp = mateInfo.Hp;
            mateDbInfo.Mp = mateInfo.Mp;
            mateDbInfo.Level = mateInfo.Level;
            mateDbInfo.Xp = mateInfo.Experience;
            mateDbInfo.Mileage = mateInfo.Mileage;
            mateDbInfo.Name = mateInfo.Name;
            mateDbInfo.UpdatedAt = DateTime.UtcNow;
        }
        MateManager.Instance.RemoveActiveMateAndDespawn(Owner, mateInfo);
    }

    public void Load(MySqlConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM mates WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", Owner.Id);
            command.Prepare();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var template = new MateDb
                    {
                        Id = reader.GetUInt32("id"),
                        ItemId = reader.GetUInt64("item_id"),
                        Name = reader.GetString("name"),
                        Xp = reader.GetInt32("xp"),
                        Level = reader.GetUInt16("level"),
                        Mileage = reader.GetInt32("mileage"),
                        Hp = reader.GetInt32("hp"),
                        Mp = reader.GetInt32("mp"),
                        Owner = reader.GetUInt32("owner"),
                        UpdatedAt = reader.GetDateTime("updated_at"),
                        CreatedAt = reader.GetDateTime("created_at")
                    };
                    _mates.Add(template.ItemId, template);
                }
            }
        }
    }

    public void Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        if (_removedMates.Count > 0)
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText = "DELETE FROM mates WHERE owner = @owner AND id IN(" + string.Join(",", _removedMates) + ")";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.Prepare();
                command.ExecuteNonQuery();
                _removedMates.Clear();
            }
        }

        foreach (var (_, value) in _mates)
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                command.CommandText =
                    "REPLACE INTO mates(`id`,`item_id`,`name`,`xp`,`level`,`mileage`,`hp`,`mp`,`owner`,`updated_at`,`created_at`) " +
                    "VALUES (@id, @item_id, @name, @xp, @level, @mileage, @hp, @mp, @owner, @updated_at, @created_at)";
                command.Parameters.AddWithValue("@id", value.Id);
                command.Parameters.AddWithValue("@item_id", value.ItemId);
                command.Parameters.AddWithValue("@name", value.Name);
                command.Parameters.AddWithValue("@xp", value.Xp);
                command.Parameters.AddWithValue("@level", value.Level);
                command.Parameters.AddWithValue("@mileage", value.Mileage);
                command.Parameters.AddWithValue("@hp", value.Hp);
                command.Parameters.AddWithValue("@mp", value.Mp);
                command.Parameters.AddWithValue("@owner", value.Owner);
                command.Parameters.AddWithValue("@updated_at", value.UpdatedAt);
                command.Parameters.AddWithValue("@created_at", value.CreatedAt);
                command.ExecuteNonQuery();
            }
        }
    }
}

public class MateDb
{
    public uint Id { get; set; }
    public ulong ItemId { get; set; }
    public string Name { get; set; }
    public int Xp { get; set; }
    public ushort Level { get; set; }
    public int Mileage { get; set; }
    public int Hp { get; set; }
    public int Mp { get; set; }
    public uint Owner { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
