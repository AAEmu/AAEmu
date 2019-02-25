using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using MySql.Data.MySqlClient;

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterBlocked
    {
        public Character Owner { get; set; }
        public Dictionary<uint, BlockedTemplate> BlockedIdList { get; set; } // bvId, Template
        private readonly List<uint> _removedBlocked; // blockedId


        public CharacterBlocked(Character owner)
        {
            Owner = owner;
            BlockedIdList = new Dictionary<uint, BlockedTemplate>();
            _removedBlocked = new List<uint>();
        }

        public void Send()
        {
            if (BlockedIdList.Count <= 0) return;
            var allBlocked = BlockUserManager.Instance.GetBlockedInfo(new List<uint>(BlockedIdList.Keys));
            var allBlockedArray = new Blocked[allBlocked.Count];
            allBlocked.CopyTo(allBlockedArray, 0);
            Owner.SendPacket(new SCBlockedUsersPacket(allBlockedArray.Length, allBlockedArray));
        }

        public void Load(MySqlConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM blocked WHERE `owner` = @owner";
                command.Parameters.AddWithValue("@owner", Owner.Id);
                command.Prepare();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var template = new BlockedTemplate()
                        {
                            Id = reader.GetUInt32("id"),
                            BlockedId = reader.GetUInt32("blocked_id"),
                            Owner = reader.GetUInt32("owner")
                        };
                        BlockedIdList.Add(template.BlockedId, template);
                    }
                }
            }
        }

        public void Save(MySqlConnection connection, MySqlTransaction transaction)
        {
            if (_removedBlocked.Count > 0)
            {
                using (var command = connection.CreateCommand())
                {
                    command.Connection = connection;
                    command.Transaction = transaction;

                    command.CommandText = "DELETE FROM blocked WHERE owner = @owner AND blocked_id IN(" + string.Join(",", _removedBlocked) + ")";
                    command.Prepare();
                    command.Parameters.AddWithValue("@owner", Owner.Id);
                    command.ExecuteNonQuery();
                    _removedBlocked.Clear();
                }
            }

            foreach (var (_, value) in BlockedIdList)
            {
                using (var command = connection.CreateCommand())
                {
                    command.Connection = connection;
                    command.Transaction = transaction;

                    command.CommandText = "REPLACE INTO blocked(`id`,`blocked_id`,`owner`) VALUES (@id, @blocked_id, @owner)";
                    command.Parameters.AddWithValue("@id", value.Id);
                    command.Parameters.AddWithValue("@blocked_id", value.BlockedId);
                    command.Parameters.AddWithValue("@owner", value.Owner);
                    command.ExecuteNonQuery();
                }
            }

        }

        public void AddBlockedUser(string name)
        {
            var blocked = BlockUserManager.Instance.GetBlockedInfo(name);
            if (blocked == null || BlockedIdList.ContainsKey(blocked.CharacterId)) return; // already blocked
            var template = new BlockedTemplate()
            {
                Id = BlockedIdManager.Instance.GetNextId(),
                BlockedId = blocked.CharacterId,
                Owner = Owner.Id
            };
            BlockedIdList.Add(blocked.CharacterId, template);
            BlockUserManager.Instance.AddToBlockList(template);
            Owner.SendPacket(new SCAddBlockedUserPacket(blocked.CharacterId, blocked.Name, true, 0));
        }


        public void RemoveBlockedUser(string name)
        {
            var blocked = BlockUserManager.Instance.GetBlockedInfo(name);
            if (blocked == null || !BlockedIdList.ContainsKey(blocked.CharacterId)) return; // not blocked
            BlockUserManager.Instance.RemoveFromAllBlocked(BlockedIdList[blocked.CharacterId].Id);
            BlockedIdList.Remove(blocked.CharacterId);
            _removedBlocked.Add(blocked.CharacterId);
            Owner.SendPacket(new SCDeleteBlockedUserPacket(blocked.CharacterId, true, name, 0));
        }

    }
}
