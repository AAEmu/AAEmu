using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Login;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Core.Packets.G2L;
using AAEmu.Game.Core.Packets.Proxy;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Team;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

public class EnterWorldManager : Singleton<EnterWorldManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, ulong> _accounts;

    protected EnterWorldManager()
    {
        _accounts = new Dictionary<uint, ulong>();
    }

    public void AddAccount(ulong accountId, uint connectionId)
    {
        var connection = LoginNetwork.Instance.GetConnection();
        var gsId = AppConfiguration.Instance.Id;

        if (AccountManager.Instance.Contains(accountId))
            connection.SendPacket(new GLPlayerEnterPacket(connectionId, gsId, 1));
        else
        {
            _accounts.Add(connectionId, accountId);
            connection.SendPacket(new GLPlayerEnterPacket(connectionId, gsId, 0));
        }
    }

    public void Login(GameConnection connection, ulong accountId, uint token)
    {
        if (_accounts.ContainsKey(token))
        {
            if (_accounts[token] == accountId)
            {
                _accounts.Remove(token);

                connection.AccountId = accountId;
                connection.State = GameState.Lobby;

                AccountManager.Instance.Add(connection);
                StreamManager.Instance.AddToken(connection.AccountId, connection.Id);

                var port = AppConfiguration.Instance.StreamNetwork.Port;
                var gm = connection.GetAttribute("gmFlag") != null;
                connection.SendPacket(new X2EnterWorldResponsePacket(0, gm, connection.Id, port, connection));
                connection.SendPacket(new ChangeStatePacket(0));
            }
            else
            {
                // TODO ...
            }
        }
        else
        {
            // TODO ...
        }
    }

    public static void Leave(GameConnection connection, byte type)
    {
        switch (type)
        {
            case 0: // выход из игры, quit game
            case 1: // выход к списку персонажей, go to character select
                if (connection.State == GameState.World)
                {

                    if (connection.LeaveTask != null)
                    {
                        break;
                    }
                    
                    // Say goodbye if player is quitting (but not going to character select)
                    if (type == 0)
                        connection.ActiveChar?.SendMessage(ChatType.System, AppConfiguration.Instance.World.LogoutMessage);

                    int logoutTime = 10000; // in ms

                    // Make it 5 minutes if you're still in combat
                    if (connection.ActiveChar?.IsInBattle ?? false)
                        logoutTime *= 30;

                    // Add 10 minutes if you have a Slave Active
                    if (SlaveManager.Instance.GetSlaveByOwnerObjId(connection.ActiveChar?.ObjId ?? 0) != null)
                        logoutTime += 1000 * 60 * 10;

                    connection.SendPacket(new SCPrepareLeaveWorldPacket(logoutTime, type, false));

                    connection.CancelTokenSource = new CancellationTokenSource();
                    var token = connection.CancelTokenSource.Token;
                    connection.LeaveTask = Task.Run(async () =>
                    {
                        await Task.Delay(logoutTime, token);
                        LeaveWorldTask(connection, type);
                    }, token);
                }

                break;
            case 2: // выбор сервера, server select
                if (connection.State == GameState.Lobby)
                {
                    var gsId = AppConfiguration.Instance.Id;
                    LoginNetwork
                        .Instance
                        .GetConnection()
                        .SendPacket(new GLPlayerReconnectPacket(gsId, connection.AccountId, connection.Id));
                }

                break;
            default:
                Logger.Warn("[Leave] Unknown type: {0}", type);
                break;
        }
    }

    public static void LeaveWorldTask(GameConnection connection, byte target)
    {
        if (connection.ActiveChar != null)
        {
            connection.ActiveChar.DisabledSetPosition = true;
            connection.ActiveChar.IsOnline = false;
            connection.ActiveChar.LeaveTime = DateTime.UtcNow;

            // Remove all remaining quest timer tasks
            QuestManager.Instance.RemoveQuestTimer(connection.ActiveChar.Id, 0);

            // Despawn and unmount everybody from owned Mates
            MateManager.Instance.RemoveAndDespawnAllActiveOwnedMates(connection.ActiveChar);
            SlaveManager.Instance.RemoveAndDespawnAllActiveOwnedSlaves(connection.ActiveChar);

            // Check if still mounted on somebody else's mount and dismount that if needed
            connection.ActiveChar.ForceDismount(AttachUnitReason.PrefabChanged); // Dismounting a mount because of unsummoning sends "10" for this

            // Remove from Team (raid/party)
            TeamManager.Instance.MemberRemoveFromTeam(connection.ActiveChar, connection.ActiveChar, RiskyAction.Leave);

            // Remove from all Chat
            ChatManager.Instance.LeaveAllChannels(connection.ActiveChar);

            // Handle Family
            if (connection.ActiveChar.Family > 0)
                FamilyManager.Instance.OnCharacterLogout(connection.ActiveChar);

            // Handle Guild
            connection.ActiveChar.Expedition?.OnCharacterLogout(connection.ActiveChar);

            // Remove player from world (hides and release Id)
            connection.ActiveChar.Delete();
            // ObjectIdManager.Instance.ReleaseId(_connection.ActiveChar.ObjId);

            // Cancel auto-regen
            //_connection.ActiveChar.StopRegen();

            // Clear Buyback table
            connection.ActiveChar.BuyBackItems.Wipe();

            // Remove subscribers
            foreach (var subscriber in connection.ActiveChar.Subscribers)
                subscriber.Dispose();
        }

        connection.SaveAndRemoveFromWorld();
        connection.State = GameState.Lobby;
        connection.LeaveTask = null;
        connection.SendPacket(new SCLeaveWorldGrantedPacket(target));
        connection.SendPacket(new ChangeStatePacket(0));
    }
}
