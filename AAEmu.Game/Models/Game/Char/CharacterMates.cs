using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Mate;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Char
{
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

        private MateDb GetMateInfo(ulong itemId)
        {
            return _mates.ContainsKey(itemId) ? _mates[itemId] : null;
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
                Name = LocalizationManager.Instance.Get("npcs","name",npctemplate.Id,npctemplate.Name), // npctemplate.Name,
                Owner = Owner.Id,
                Mileage = 0,
                Xp = ExpirienceManager.Instance.GetExpForLevel(50, true),
                Hp = 9999,
                Mp = 9999,
                UpdatedAt = DateTime.Now,
                CreatedAt = DateTime.Now
            };
            _mates.Add(template.ItemId, template);
            return template;
        }

        public void SpawnMount(SkillItem skillData)
        {
            var activeMate = MateManager.Instance.GetActiveMate(Owner.ObjId);
            if (activeMate != null)
            {
                DespawnMate(activeMate.TlId);
                return;
            }

            var item = Owner.Inventory.GetItemById(skillData.ItemId);
            if (item == null) return;

            var itemTemplate = (SummonMateTemplate)ItemManager.Instance.GetTemplate(item.TemplateId);
            var npcId = itemTemplate.NpcId;
            var template = NpcManager.Instance.GetTemplate(npcId);
            var tlId = (ushort)TlIdManager.Instance.GetNextId();
            var objId = ObjectIdManager.Instance.GetNextId();
            var mateDbInfo = GetMateInfo(skillData.ItemId) ?? CreateNewMate(skillData.ItemId, template); // TODO - new name

            var mount = new Mount
            {
                ObjId = objId,
                TlId = tlId,
                OwnerId = Owner.Id,
                Name = mateDbInfo.Name,
                TemplateId = template.Id,
                Template = template,
                ModelId = template.ModelId,
                Faction = Owner.Faction,
                Level = (byte)mateDbInfo.Level,
                Hp = mateDbInfo.Hp,
                Mp = mateDbInfo.Mp,
                Position = Owner.Position.Clone(),
                OwnerObjId = Owner.ObjId,

                Id = mateDbInfo.Id,
                ItemId = mateDbInfo.ItemId,
                UserState = 1, // TODO
                Exp = mateDbInfo.Xp,
                Mileage = mateDbInfo.Mileage,
                SpawnDelayTime = 0, // TODO
            };
            foreach (var skill in MateManager.Instance.GetMateSkills(npcId))
            {
                mount.Skills.Add(skill);
            }

            var (newX, newY) = MathUtil.AddDistanceToFront(3, mount.Position.X, mount.Position.Y, mount.Position.RotationZ);
            mount.Position.X = newX;
            mount.Position.Y = newY;

            MateManager.Instance.AddActiveMateAndSpawn(Owner, mount, item);
        }

        public void DespawnMate(uint tlId)
        {
            var mateInfo = MateManager.Instance.GetActiveMateByTlId(tlId);
            if (mateInfo != null)
            {
                var mateDbInfo = GetMateInfo(mateInfo.ItemId);
                if (mateDbInfo != null)
                {
                    mateDbInfo.Hp = mateInfo.Hp;
                    mateDbInfo.Mp = mateInfo.Mp;
                    mateDbInfo.Level = mateInfo.Level;
                    mateDbInfo.Xp = mateInfo.Exp;
                    mateDbInfo.Mileage = mateInfo.Mileage;
                    mateDbInfo.Name = mateInfo.Name;
                    mateDbInfo.UpdatedAt = DateTime.Now;
                }
            }

            MateManager.Instance.RemoveActiveMateAndDespawn(Owner, tlId);
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
                    command.Prepare();
                    command.Parameters.AddWithValue("@owner", Owner.Id);
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
}
