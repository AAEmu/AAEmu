using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.World.Transform;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class FriendMananger : Singleton<FriendMananger>
    {
        private readonly Logger _log = LogManager.GetCurrentClassLogger();
        private Dictionary<uint, FriendTemplate> _allFriends; // temp id, template

        public void Load()
        {
            _allFriends = new Dictionary<uint, FriendTemplate>();

            _log.Info("Loading friends ...");
            using (var connection = MySQL.CreateConnection())
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM friends";
                    command.Prepare();
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var template = new FriendTemplate()
                            {
                                Id = reader.GetUInt32("id"),
                                FriendId = reader.GetUInt32("friend_id"),
                                Owner = reader.GetUInt32("owner")
                            };
                            _allFriends.Add(template.Id, template);
                        }
                    }
                }
            }

            _log.Info("Loaded {0} friends", _allFriends.Count);
        }

        public void AddToAllFriends(FriendTemplate template)
        {
            if (!_allFriends.ContainsKey(template.Id)) _allFriends.Add(template.Id, template);
        }

        public void RemoveFromAllFriends(uint id)
        {
            if (_allFriends.ContainsKey(id)) _allFriends.Remove(id);
        }

        public void SendStatusChange(Character unit, bool forOnline, bool boolean)
        {
            if (_allFriends.Count <= 0) return;
            foreach (var (_, value) in _allFriends)
            {
                if (value.FriendId != unit.Id) continue;

                var friendOwner = WorldManager.Instance.GetCharacterById(value.Owner);
                if (friendOwner != null)
                {
                    var myInfos = FormatFriend(unit);
                    if (forOnline)
                        myInfos.IsOnline = boolean;
                    else
                        myInfos.InParty = boolean;
                    friendOwner.SendPacket(new SCFriendStatusChangedPacket(myInfos));
                }
            }
        }

        public List<Friend> GetFriendInfo(List<uint> ids)
        {
            var friendsList = new List<Friend>();
            var offlineIds = new List<uint>();
            foreach (var id in ids)
            {
                var friend = WorldManager.Instance.GetCharacterById(id);
                if (friend == null)
                {
                    offlineIds.Add(id);
                    continue;
                }

                var newFriend = FormatFriend(friend);
                friendsList.Add(newFriend);
            }

            if (offlineIds.Count <= 0) return friendsList;

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
                            var template = new Friend
                            {
                                Name = reader.GetString("name"),
                                CharacterId = reader.GetUInt32("id"),
                                Position = new Transform(null, null, 1, reader.GetUInt32("zone_id"), 1, reader.GetFloat("x"), reader.GetFloat("y"), reader.GetFloat("z"), 0, 0, 0),
                                InParty = false,
                                IsOnline = false,
                                Race = (Race)reader.GetUInt32("race"),
                                Level = reader.GetByte("level"),
                                LastWorldLeaveTime = reader.GetDateTime("leave_time"),
                                Health = reader.GetInt32("hp"),
                                Ability1 = (AbilityType)reader.GetByte("ability1"),
                                Ability2 = (AbilityType)reader.GetByte("ability2"),
                                Ability3 = (AbilityType)reader.GetByte("ability3")
                            };
                            friendsList.Add(template);
                        }
                    }
                }
            }

            return friendsList;
        }

        public Friend GetFriendInfo(string name)
        {
            var friend = WorldManager.Instance.GetCharacter(name);
            if (friend != null) return FormatFriend(friend);

            uint friendId = 0;
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
                            friendId = reader.GetUInt32("id");
                        }
                    }
                }
            }

            var friendInfo = GetFriendInfo(new List<uint> {friendId});
            return friendInfo.Count > 0 ? GetFriendInfo(new List<uint> {friendId})[0] : null;
        }

        private static Friend FormatFriend(Character friend)
        {
            return new Friend()
            {
                Name = friend.Name,
                CharacterId = friend.Id,
                Position = friend.Transform.Clone(),
                InParty = friend.InParty,
                IsOnline = true,
                Race = friend.Race,
                Level = friend.Level,
                LastWorldLeaveTime = friend.LeaveTime,
                Health = friend.Hp,
                Ability1 = friend.Ability1,
                Ability2 = friend.Ability2,
                Ability3 = friend.Ability3
            };
        }
    }

    public class FriendTemplate
    {
        public uint Id { get; set; }
        public uint FriendId { get; set; }
        public uint Owner { get; set; }
    }
}
