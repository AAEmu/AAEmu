using System.Collections.Generic;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using MySql.Data.MySqlClient;
using NLog;

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterBlocked
    {
        public Character Owner { get; set; }
        public Dictionary<uint, BlockedTemplate> BlockedList { get; set; } // bvId, Template
        private readonly List<uint> _removedBlocked; // blockedId

        private static Logger _log = LogManager.GetCurrentClassLogger();

        public CharacterBlocked(Character owner)
        {
            Owner = owner;
            BlockedList = new Dictionary<uint, BlockedTemplate>();
            _removedBlocked = new List<uint>();
        }

        public List<Blocked> GetBlockedInfo(List<uint> ids)
        {
            var blockedList = new List<Blocked>();
            var offlineIds = new List<uint>();
            foreach (var id in ids)
            {
                var friend = WorldManager.Instance.GetCharacterById(id);
                if (friend == null)
                {
                    offlineIds.Add(id);
                    continue;
                }

                var newBlocked = FormatBlocked(friend);
                blockedList.Add(newBlocked);
            }

            if (offlineIds.Count <= 0) return blockedList;

            using (var connection = MySQL.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM characters WHERE id IN(" + string.Join(",", offlineIds) + ")";
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var template = new Blocked
                            {
                                Name = reader.GetString("name"),
                                CharacterId = reader.GetUInt32("id"),
                            };
                            blockedList.Add(template);
                        }
                    }
                }
            }
            return blockedList;
        }

        public void Send()
        {

            if (BlockedList.Count <= 0) return;
            var allBlocked = GetBlockedInfo(new List<uint>(BlockedList.Keys));
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
                            Owner = reader.GetUInt32("owner"),
                            BlockedId = reader.GetUInt32("blocked_id")
                        };
                        BlockedList.Add(template.BlockedId, template);
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
                    command.Parameters.AddWithValue("@owner", Owner.Id);
                    command.Prepare();
                    command.ExecuteNonQuery();
                    _removedBlocked.Clear();
                }
            }

            foreach (var (_, value) in BlockedList)
            {
                using (var command = connection.CreateCommand())
                {
                    command.Connection = connection;
                    command.Transaction = transaction;

                    command.CommandText = "REPLACE INTO blocked(`owner`,`blocked_id`) VALUES (@owner, @blocked_id)";
                    command.Parameters.AddWithValue("@owner", value.Owner);
                    command.Parameters.AddWithValue("@blocked_id", value.BlockedId);
                    command.ExecuteNonQuery();
                }
            }
        }
               

        public void AddBlockedUser(string name)
        {
            var blocked = WorldManager.Instance.GetCharacter(name);

            if (blocked == null || BlockedList.ContainsKey(blocked.Id)) return; // already blocked
            var template = new BlockedTemplate()
            {
                BlockedId = blocked.Id,
                Owner = Owner.Id
            };
            BlockedList.Add(blocked.Id, template);
            Owner.SendPacket(new SCAddBlockedUserPacket(blocked.Id, blocked.Name, true, 0));
        }


        public void RemoveBlockedUser(string name)
        {
            var blocked = WorldManager.Instance.GetCharacter(name);
            if (blocked == null || !BlockedList.ContainsKey(blocked.Id)) return; // not blocked
            BlockedList.Remove(blocked.Id);
            _removedBlocked.Add(blocked.Id);
            Owner.SendPacket(new SCDeleteBlockedUserPacket(blocked.Id, true, name, 0));
        }

        private Blocked FormatBlocked(Character blocked)
        {
            return new Blocked()
            {
                CharacterId = blocked.Id,
                Name = blocked.Name
            };
        }

    }

    public class BlockedTemplate
    {
        public uint Owner { get; set; }
        public uint BlockedId { get; set; }
    }
}
