using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.DB.Game;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils.DB;
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

            using (var ctx = new GameDBContext())
            {
                _allFriends = ctx.Friends.ToDictionary(f => (uint)f.Id, f => (FriendTemplate)f);
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

            if (offlineIds.Count <= 0) 
                return friendsList;

            using (var ctx = new GameDBContext())
            {
                friendsList.AddRange(
                    ctx.Characters.Where(c => 
                    offlineIds.Contains((uint)c.Id))
                    .ToArray()
                    .Select(c=>(Friend)c)
                    .Select(c =>
                    {
                        c.IsOnline = false;
                        c.InParty = false;
                        return c;
                    })
                    .ToArray());
            }

            return friendsList;
        }

        public Friend GetFriendInfo(string name)
        {
            var friend = WorldManager.Instance.GetCharacter(name);
            if (friend != null) return FormatFriend(friend);

            uint friendId = 0;
            using (var ctx = new GameDBContext())
                friendId = ctx.Characters.Where(f => f.Name == name).Select(s=>s.Id).FirstOrDefault();

            var friendInfo = GetFriendInfo(new List<uint> {friendId});
            return friendInfo.Count > 0 ? GetFriendInfo(new List<uint> {friendId})[0] : null;
        }

        private static Friend FormatFriend(Character friend)
        {
            return new Friend()
            {
                Name = friend.Name,
                CharacterId = friend.Id,
                Position = friend.Position.Clone(),
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

        public DB.Game.Friends ToEntity()
            =>
            new Friends()
            {
                Id       = this.Id       ,
                FriendId = this.FriendId ,
                Owner    = this.Owner    ,
            };

        public static explicit operator FriendTemplate(Friends v)
            =>
            new FriendTemplate()
            {
                Id       = v.Id       ,
                FriendId = v.FriendId ,
                Owner    = v.Owner    ,
            };
    }
}
