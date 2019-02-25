using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.DB;
using AAEmu.Game.Models.Game;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class BlockUserManager : Singleton<BlockUserManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private Dictionary<uint, BlockedTemplate> _allblocked; // temp id, template

        public void Load()
        {
            _allblocked = new Dictionary<uint, BlockedTemplate>();

            _log.Info("Loading Blocked Users ...");
            using (var connection = MySQL.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM blocked";
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
                            _allblocked.Add(template.Id, template);
                        }
                    }
                }
            }
            _log.Info("Loaded {0} blocked", _allblocked.Count);
        }

        public void AddToBlockList(BlockedTemplate template)
        {
            if (!_allblocked.ContainsKey(template.Id)) _allblocked.Add(template.Id, template);
        }

        public void RemoveFromAllBlocked(uint id)
        {
            if (_allblocked.ContainsKey(id)) _allblocked.Remove(id);
        }


        public List<Blocked> GetBlockedInfo(List<uint> ids)
        {
            var blockedList = new List<Blocked>();
            var offlineIds = new List<uint>();
            foreach (var id in ids)
            {
                var blocked = WorldManager.Instance.GetCharacterById(id);
                if (blocked == null)
                {
                    offlineIds.Add(id);
                    continue;
                }

                var newBlocked = FormatBlocked(blocked);
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

        public Blocked GetBlockedInfo(string name)
        {
            var blocked = WorldManager.Instance.GetCharacter(name);
            if (blocked != null) return FormatBlocked(blocked);

            uint blockedId = 0;
            using (var connection = MySQL.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM characters WHERE `name` = @name";
                    command.Parameters.AddWithValue("@name", name);
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            blockedId = reader.GetUInt32("id");
                        }
                    }
                }
            }

            var blockedInfo = GetBlockedInfo(new List<uint> { blockedId });
            return blockedInfo.Count > 0 ? GetBlockedInfo(new List<uint> { blockedId })[0] : null;
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
        public uint Id { get; set; }
        public uint BlockedId { get; set; }
        public uint Owner { get; set; }
    }
}
