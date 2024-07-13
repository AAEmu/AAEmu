﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Network.Connections;

public enum GameState
{
    Lobby,
    World
}

public class GameConnection
{
    private ISession _session;

    public uint Id => _session.SessionId;
    public ulong AccountId { get; set; }
    public IPAddress Ip => _session.Ip;
    public PacketStream LastPacket { get; set; }
    public AccountPayment Payment { get; set; }
    public int PacketCount { get; set; }
    public List<IDisposable> Subscribers { get; set; }
    public GameState State { get; set; }
    public Character ActiveChar { get; set; }
    public Dictionary<uint, Character> Characters;
    public Dictionary<uint, House> Houses;
    public object WriteLock { get; set; }
    public object ReadLock { get; set; }
    public byte LastCount { get; set; }
    public Task LeaveTask { get; set; }
    public CancellationTokenSource CancelTokenSource { get; set; }
    public DateTime LastPing { get; set; }

    public GameConnection(ISession session)
    {
        _session = session;
        Subscribers = new List<IDisposable>();

        Characters = new Dictionary<uint, Character>();
        Houses = new Dictionary<uint, House>();
        Payment = new AccountPayment(this);
        WriteLock = new object();
        ReadLock = new object();
        // AddAttribute("gmFlag", true);
    }

    public void SendPacket(GamePacket packet)
    {
        lock (WriteLock)
        {
            packet.Connection = this;
            SendPacket(packet.Encode());
        }
    }

    public void SendPacket(byte[] packet)
    {
        _session?.SendPacket(packet);
    }

    public static void OnConnect()
    {
    }

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

        SaveAndRemoveFromWorld();
        AccountManager.Instance.UpdateLoginTime(AccountId, DateTime.UtcNow);
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

        Houses.Clear();
        HousingManager.Instance.GetByAccountId(Houses, AccountId);
    }

    /// <summary>
    /// Called when closing a connection
    /// </summary>
    public void SaveAndRemoveFromWorld()
    {
        // TODO: this needs a rewrite
        if (ActiveChar == null)
            return;

        // Remove Radars
        RadarManager.Instance.UnRegister(ActiveChar);

        ActiveChar.Delete();
        // Removed ReleaseId here to try and fix party/raid disconnect and reconnect issues. Replaced with saving the data
        //ObjectIdManager.Instance.ReleaseId(ActiveChar.ObjId);

        // Do a manual save here as it's no longer in _characters at this point
        // TODO: might need a better option like saving this transaction for later to be used by the SaveMananger
        ActiveChar.SaveDirectlyToDatabase();
    }
}
