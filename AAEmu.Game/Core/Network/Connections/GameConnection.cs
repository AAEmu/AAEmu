using System.Net;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;

namespace AAEmu.Game.Core.Network.Connections;

public class GameConnection
{
    private ISession _session;

    public uint Id => _session.SessionId;
    public uint AccountId { get; set; }
    public IPAddress Ip => _session.Ip;
    public PacketStream LastPacket { get; set; }
    public AccountPayment Payment { get; set; }
    public int PacketCount { get; set; }
    public List<IDisposable> Subscribers { get; set; }
    public GameState State { get; set; }
    public Character ActiveChar { get; set; }
    public Dictionary<uint, Character> Characters { get; set; }
    public Dictionary<uint, House> Houses { get; set; }
    public Task LeaveTask { get; set; }
    public CancellationTokenSource CancelTokenSource { get; set; }
    public DateTime LastPing { get; set; }

    public GameConnection(ISession session)
    {
        _session = session;
        Subscribers = [];

        Characters = [];
        Houses = [];
        Payment = new AccountPayment(this);
        // AddAttribute("gmFlag", true);
    }

    /// <summary>
    /// Encodes and sends a single game packet to the active connection
    /// </summary>
    /// <param name="packet"></param>
    public void SendPacket(GamePacket packet)
    {
        packet.Connection = this;
        SendPacket(packet.Encode());
    }

    /// <summary>
    /// Sends RAW packet data to the active connection
    /// </summary>
    /// <param name="packet"></param>
    private void SendPacket(byte[] packet)
    {
        _session?.SendPacket(packet);
    }

    /// <summary>
    /// On connect event
    /// </summary>
    public void OnConnect()
    {
        //
    }

    /// <summary>
    /// On Disconnect event
    /// </summary>
    public void OnDisconnect()
    {
        AccountManager.Instance.Remove(AccountId);

        if (ActiveChar != null)
        {
            foreach (var subscriber in ActiveChar.Subscribers)
                subscriber.Dispose();

            ActiveChar.Events?.OnDisconnect(this, new OnDisconnectArgs { Player = ActiveChar });
            ActiveChar.RemoveAndDespawnActiveOwnedMatesSlaves();
        }

        foreach (var subscriber in Subscribers)
            subscriber.Dispose();

        SaveAndRemoveFromWorld(ActiveChar);
        AccountManager.Instance.UpdateLoginTime(AccountId, DateTime.UtcNow);
    }

    /// <summary>
    /// Closes the active connection session
    /// </summary>
    public void Shutdown()
    {
        _session?.Close();
    }

    /// <summary>
    /// Adds a named attribute object to the connection 
    /// </summary>
    /// <param name="name"></param>
    /// <param name="value"></param>
    public void AddAttribute(string name, object value)
    {
        _session.AddAttribute(name, value);
    }

    /// <summary>
    /// Gets a named attribute of the connection
    /// </summary>
    /// <param name="name"></param>
    /// <returns></returns>
    public object GetAttribute(string name)
    {
        return _session.GetAttribute(name);
    }

    /// <summary>
    /// Adds a subscriber object
    /// </summary>
    /// <param name="disposable"></param>
    public void PushSubscriber(IDisposable disposable)
    {
        Subscribers.Add(disposable);
    }

    /// <summary>
    /// Loads Account data for the connection like characters and houses
    /// </summary>
    public void LoadAccount()
    {
        // TODO: Load payment and account tier information

        // Load character info for this account
        Characters.Clear();
        using (var connection = MySQL.CreateConnection())
        {
            var characterIds = new List<uint>();
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.CommandText = "SELECT id FROM characters WHERE `account_id` = @account_id and `deleted`=0";
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
                if (!CharacterManager.CheckForDeletedCharactersDeletion(character, this, connection))
                {
                    Characters.Add(character.Id, character);
                }
            }

            /*
            foreach (var character in Characters.Values)
                character.Inventory.Load(connection, SlotType.Equipment);
            */
        }

        // Load housing info for this account
        Houses.Clear();
        HousingManager.Instance.GetByAccountId(Houses, AccountId);
    }

    /// <summary>
    /// Called when closing a connection
    /// </summary>
    public static void SaveAndRemoveFromWorld(Character activeChar)
    {
        // TODO: this needs a rewrite
        if (activeChar == null)
            return;

        // Remove Radars
        RadarManager.Instance.UnRegister(activeChar);

        activeChar.Delete();
        // Removed ReleaseId here to try and fix party/raid disconnect and reconnect issues. Replaced with saving the data
        //ObjectIdManager.Instance.ReleaseId(ActiveChar.ObjId);

        // Do a manual save here as it's no longer in _characters at this point
        // TODO: might need a better option like saving this transaction for later to be used by the SaveMananger
        activeChar.SaveDirectlyToDatabase();
    }
}
