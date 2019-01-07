using System;
using System.Collections.Generic;
using System.Net;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
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

        public List<IDisposable> Subscribers { get; set; }
        public GameState State { get; set; }
        public Character ActiveChar { get; set; }
        public readonly Dictionary<uint, Character> Characters;

        public Task LeaveTask { get; set; }

        public GameConnection(Session session)
        {
            _session = session;
            Characters = new Dictionary<uint, Character>();
            Subscribers = new List<IDisposable>();
        }

        public void SendPacket(GamePacket packet)
        {
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

        public void LoadCharacters()
        {
            Characters.Clear();
            using (var connection = MySQL.CreateConnection())
            {
                var characterIds = new List<uint>();
                using (var command = connection.CreateCommand())
                {
                    command.Connection = connection;
                    command.CommandText = "SELECT id FROM characters WHERE `account_id` = @account_id";
                    command.Parameters.AddWithValue("@account_id", AccountId);
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                            characterIds.Add(reader.GetUInt32("id"));
                    }
                }

                foreach (var id in characterIds)
                {
                    var character = Character.Load(connection, id, AccountId);
                    if (character == null)
                        continue; // TODO ...
                    Characters.Add(character.Id, character);
                }

                foreach (var character in Characters.Values)
                    character.Inventory.Load(connection, SlotType.Equipment);
            }
        }

        public void SetDeleteCharacter(uint characterId)
        {
            if (Characters.ContainsKey(characterId))
            {
                var character = Characters[characterId];
                character.DeleteRequestTime = DateTime.UtcNow;
                character.DeleteTime = character.DeleteRequestTime.AddDays(7); // TODO to config...
                SendPacket(new SCDeleteCharacterResponsePacket(character.Id, 2, character.DeleteRequestTime,
                    character.DeleteTime));

                using (var connection = MySQL.CreateConnection())
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText =
                            "UPDATE characters SET `delete_request_time` = @delete_request_time, `delete_time` = @delete_time WHERE `id` = @id";
                        command.Prepare();
                        command.Parameters.AddWithValue("@delete_request_time", character.DeleteRequestTime);
                        command.Parameters.AddWithValue("@delete_time", character.DeleteTime);
                        command.Parameters.AddWithValue("@id", character.Id);
                        command.ExecuteNonQuery();
                    }
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

                using (var connection = MySQL.CreateConnection())
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText =
                            "UPDATE characters SET `delete_request_time` = @delete_request_time, `delete_time` = @delete_time WHERE `id` = @id";
                        command.Prepare();
                        command.Parameters.AddWithValue("@delete_request_time", character.DeleteRequestTime);
                        command.Parameters.AddWithValue("@delete_time", character.DeleteTime);
                        command.Parameters.AddWithValue("@id", character.Id);
                        command.ExecuteNonQuery();
                    }
                }
            }
            else
            {
                SendPacket(new SCCancelCharacterDeleteResponsePacket(characterId, 4));
            }
        }

        public void Save()
        {
            ActiveChar?.Save();
        }
    }
}