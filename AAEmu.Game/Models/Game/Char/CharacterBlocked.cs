using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.DB.Game;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Utils.DB;
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

            using (var ctx = new GameDBContext())
            {
                blockedList.AddRange(
                    ctx.Characters
                    .Where(c => offlineIds.Contains(c.Id))
                    .ToList()
                    .Select(c => new Blocked() {
                        Name = c.Name,
                        CharacterId = (uint)c.Id
                    })
                    .ToList());
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


        public void Load(GameDBContext ctx)
        {
            BlockedList = BlockedList.Concat(
                ctx.Blocked
                .Where(b => b.Owner == Owner.Id)
                .ToList()
                .Select(b => (BlockedTemplate)b)
                .ToDictionary(b => b.BlockedId, b => b)
                )
                .GroupBy(i => i.Key).ToDictionary(group => group.Key, group => group.First().Value);
        }

        public void Save(GameDBContext ctx)
        {
            ctx.Blocked.RemoveRange(
                ctx.Blocked.Where(b => b.Owner == Owner.Id && _removedBlocked.Contains((uint)b.BlockedId)));
            ctx.SaveChanges();

            _removedBlocked.Clear();

            foreach (var value in BlockedList.Values)
            {
                ctx.Blocked.RemoveRange(
                    ctx.Blocked.Where(b => b.Owner == value.Owner && (uint)b.BlockedId == value.BlockedId));
                ctx.Blocked.Add(value.ToEntity());
            }
            ctx.SaveChanges();
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

        public DB.Game.Blocked ToEntity()
            =>
            new DB.Game.Blocked()
            {
                Owner     = this.Owner     ,
                BlockedId = this.BlockedId ,
            };

        public static explicit operator BlockedTemplate(DB.Game.Blocked v)
            =>
            new BlockedTemplate()
            {
                Owner     = v.Owner     ,
                BlockedId = v.BlockedId ,
            };
    }
}
