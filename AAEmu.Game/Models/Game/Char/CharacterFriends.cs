using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Utils.DB;

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterFriends
    {
        public Character Owner { get; set; }
        public Dictionary<uint, FriendTemplate> FriendsIdList { get; set; } // friendId, Template
        private readonly List<uint> _removedFriends; // friendId

        public CharacterFriends(Character owner)
        {
            Owner = owner;
            FriendsIdList = new Dictionary<uint, FriendTemplate>();
            _removedFriends = new List<uint>();
        }

        public void AddFriend(string name)
        {
            var friend = FriendMananger.Instance.GetFriendInfo(name);
            if (friend == null || FriendsIdList.ContainsKey(friend.CharacterId))
            {
                // TODO - ERROR MESSAGE ALREADY ADDED
                return;
            }

            var template = new FriendTemplate()
            {
                Id = FriendIdManager.Instance.GetNextId(),
                FriendId = friend.CharacterId,
                Owner = Owner.Id
            };
            FriendsIdList.Add(friend.CharacterId, template);
            FriendMananger.Instance.AddToAllFriends(template);
            Owner.SendPacket(new SCAddFriendPacket(friend, true, 0));
        }

        public void RemoveFriend(string name)
        {
            var friend = FriendMananger.Instance.GetFriendInfo(name);
            if (friend == null || !FriendsIdList.ContainsKey(friend.CharacterId))
            {
                // TODO - ERROR MESSAGE NOT FRIEND
                return;
            }

            FriendMananger.Instance.RemoveFromAllFriends(FriendsIdList[friend.CharacterId].Id);
            FriendsIdList.Remove(friend.CharacterId);
            _removedFriends.Add(friend.CharacterId);
            Owner.SendPacket(new SCDeleteFriendPacket(friend.CharacterId, true, name, 0));
        }

        public void Send()
        {
            if (FriendsIdList.Count <= 0) return;

            var allFriends = FriendMananger.Instance.GetFriendInfo(new List<uint>(FriendsIdList.Keys));
            var allFriendsArray = new Friend[allFriends.Count];
            allFriends.CopyTo(allFriendsArray, 0);
            Owner.SendPacket(new SCFriendsPacket(allFriendsArray.Length, allFriendsArray));
        }

        public void Load(GameDBContext ctx)
        {
            FriendsIdList = FriendsIdList.Concat(
                ctx.Friends
                .Where(f => f.Owner == Owner.Id)
                .ToList()
                .Select(f => (FriendTemplate)f)
                .ToDictionary(f => f.FriendId, f => f)
                )
                .GroupBy(i => i.Key).ToDictionary(group => group.Key, group => group.First().Value);
        }

        public void Save(GameDBContext ctx)
        {
            if (_removedFriends.Count > 0)
            {
                ctx.Friends.RemoveRange(
                    ctx.Friends.Where(f => f.Owner == Owner.Id && _removedFriends.Contains((uint)f.FriendId)));
                _removedFriends.Clear();
            }
            ctx.SaveChanges();

            foreach (var value in FriendsIdList.Values)
            {
                ctx.Friends.RemoveRange(
                    ctx.Friends.Where(f => 
                        f.Id == value.Id && 
                        f.Owner == value.Owner));
            }
            ctx.SaveChanges();

            ctx.Friends.AddRange(FriendsIdList.Values.Select(f => f.ToEntity()));

            ctx.SaveChanges();
        }
    }
}
