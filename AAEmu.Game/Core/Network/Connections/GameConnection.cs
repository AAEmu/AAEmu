using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Tasks;
using AAEmu.Game.Utils.DB;

namespace AAEmu.Game.Core.Network.Connections
{
    public enum GameState
    {
        Lobby,
        World
    }

    public class GameConnection
    {
        private Session _session;

        public uint Id => _session.Id;
        public uint AccountId { get; set; }
        public IPAddress Ip => _session.Ip;
        public PacketStream LastPacket { get; set; }
        
        public AccountPayment Payment { get; set; }
        
        public int PacketCount { get; set; }
        
        public List<IDisposable> Subscribers { get; set; }
        public GameState State { get; set; }
        public Character ActiveChar { get; set; }
        public Dictionary<uint, Character> Characters;
        public Dictionary<uint, House> Houses;
        
        public Task LeaveTask { get; set; }

        public GameConnection(Session session)
        {
            _session = session;
            Subscribers = new List<IDisposable>();
            
            Characters = new Dictionary<uint, Character>();
            Houses = new Dictionary<uint, House>();
            Payment = new AccountPayment(this);
        }

        public void SendPacket(GamePacket packet)
        {
            packet.Connection = this;
            SendPacket(packet.Encode());
        }

        public void SendPacket(byte[] packet)
        {
            _session?.SendPacket(packet);
        }

        public void OnConnect()
        {
        }

        public void OnDisconnect()
        {
            AccountManager.Instance.Remove(AccountId);
            
            if (ActiveChar != null)
                foreach (var subscriber in ActiveChar.Subscribers)
                    subscriber.Dispose();

            foreach (var subscriber in Subscribers)
                subscriber.Dispose();

            Save();
        }

        public void Shutdown()
        {
            _session?.Close();
        }

        public void AddAttribute(string name, object value)
        {
            _session.AddAttribute(name, value);
        }

        public object GetAttribute(string name)
        {
            return _session.GetAttribute(name);
        }

        public void PushSubscriber(IDisposable disposable)
        {
            Subscribers.Add(disposable);
        }

        public void LoadAccount()
        {
            Characters.Clear();
            using (var ctx = new GameDBContext())
            {
                var items = ctx.Characters
                    .Where(c => 
                        c.AccountId == AccountId && 
                        c.Deleted == 0)
                    .ToList();

                items.Where(c => c.DeleteTime > DateTime.MinValue && c.DeleteTime < DateTime.UtcNow)
                    .All(c =>
                    {
                        c.Deleted = 1;
                        return true;
                    });
                ctx.SaveChanges();

                var q = items.Where(c => c.Deleted == 0).ToList().Select(i => Character.Load(ctx, i.Id, AccountId)).Where(c => c != null).ToArray();

                Characters = Characters.Concat(
                        items
                        .Where(c => c.Deleted == 0)
                        .ToList()
                        .Select(i => Character.Load(ctx, i.Id, AccountId))
                        .Where(c => c != null)
                        .ToDictionary(c => c.Id, c => c)
                    )
                    .GroupBy(i => i.Key).ToDictionary(group => group.Key, group => group.First().Value);

                foreach (var character in Characters.Values)
                    character.Inventory.Load(ctx, SlotType.Equipment);
            }

            Houses.Clear();
            HousingManager.Instance.GetByAccountId(Houses, AccountId);
        }

        public void SetDeleteCharacter(uint characterId)
        {
            if (Characters.ContainsKey(characterId))
            {
                var character = Characters[characterId];
                character.DeleteRequestTime = DateTime.UtcNow;
                // character.DeleteTime = character.DeleteRequestTime.AddDays(7); // TODO to config...

                // TODO: We need a config for this, but for now I added a silly if/else group
                if (character.Level <= 1)
                    character.DeleteTime = character.DeleteRequestTime.AddMinutes(5);
                else
                if (character.Level < 10)
                    character.DeleteTime = character.DeleteRequestTime.AddMinutes(30);
                else
                if (character.Level < 30)
                    character.DeleteTime = character.DeleteRequestTime.AddHours(4);
                else
                if (character.Level < 40)
                    character.DeleteTime = character.DeleteRequestTime.AddDays(1);
                else
                    character.DeleteTime = character.DeleteRequestTime.AddDays(7);

                // TODO: Delete leadership, set properties to public, remove from party/guild/family
                SendPacket(new SCDeleteCharacterResponsePacket(character.Id, 2, character.DeleteRequestTime,
                    character.DeleteTime));

                using (var ctx = new GameDBContext())
                {
                    ctx.Characters.Where(c => c.Id == character.Id).ToList().All(c =>
                    {
                        c.DeleteRequestTime = character.DeleteRequestTime;
                        c.DeleteTime = character.DeleteTime;
                        return true;
                    });
                    ctx.SaveChanges();
                }
            }
            else
            {
                SendPacket(new SCDeleteCharacterResponsePacket(characterId, 0));
            }
        }

        
        public void SetRestoreCharacter(uint characterId)
        {
            if (Characters.ContainsKey(characterId))
            {
                var character = Characters[characterId];
                character.DeleteRequestTime = DateTime.MinValue;
                character.DeleteTime = DateTime.MinValue;
                SendPacket(new SCCancelCharacterDeleteResponsePacket(character.Id, 3));

                // TODO: Verify restore actually works
                using (var ctx = new GameDBContext())
                {
                    ctx.Characters.Where(c => c.Id == character.Id).ToList().All(c =>
                    {
                        c.DeleteRequestTime = character.DeleteRequestTime;
                        c.DeleteTime = character.DeleteTime;
                        return true;
                    });
                    ctx.SaveChanges();
                }
            }
            else
            {
                SendPacket(new SCCancelCharacterDeleteResponsePacket(characterId, 4));
            }
        }

        public void Save()
        {
            if (ActiveChar == null)
                return;

            ActiveChar.Delete();
            ObjectIdManager.Instance.ReleaseId(ActiveChar.ObjId);

            ActiveChar.Save();
        }
    }
}
