using AAEmu.Commons.Cryptography;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Login;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Core.Packets.G2L;
using AAEmu.Game.Core.Packets.Proxy;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.StaticValues;

using NLog;

namespace AAEmu.Game.Core.Managers.World;

public class EnterWorldManager(
    IAccountManager accountManager,
    IStreamManager streamManager,
    IQuestManager questManager,
    ITeamManager teamManager,
    IChatManager chatManager,
    IFamilyManager familyManager,
    IWorldManager worldManager) : Singleton<EnterWorldManager>, IEnterWorldManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    /// <summary>
    /// List of connected accounts (connection token, accountId)
    /// </summary>
    private readonly Dictionary<uint, uint> _accounts = [];
    private readonly Lock _accountsLock = new();

    /// <summary>
    /// Adds an account to the connection list and notifies the login server the client is connected
    /// </summary>
    /// <param name="accountId"></param>
    /// <param name="connectionId"></param>
    public void AddAccount(uint accountId, uint connectionId)
    {
        var connection = LoginNetwork.Instance.GetConnection();
        var gsId = AppConfiguration.Instance.Id;

        if (accountManager.Contains(accountId))
            connection.SendPacket(new GLPlayerEnterPacket(connectionId, gsId, 1));
        else
        {
            // Overwrite stale pending tokens (e.g. prior CAEnterWorld without a completed X2EnterWorld).
            lock (_accountsLock)
                _accounts[connectionId] = accountId;
            connection.SendPacket(new GLPlayerEnterPacket(connectionId, gsId, 0));
        }
    }

    /// <summary>
    /// Performs an enter world event and places the connection in lobby state.
    /// Also notifies the stream server that a new connection has been made
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="accountId"></param>
    /// <param name="token"></param>
    public void Login(GameConnection connection, uint accountId, uint token)
    {
        uint expectedAccountId = 0;
        var accountMatches = false;
        var tokenExists = false;
        lock (_accountsLock)
        {
            tokenExists = _accounts.TryGetValue(token, out expectedAccountId);
            accountMatches = tokenExists && expectedAccountId == accountId;
            if (accountMatches)
                _accounts.Remove(token);
        }

        if (accountMatches)
        {
            connection.AccountId = accountId;
            connection.State = GameState.Lobby;

            accountManager.Add(connection);
            streamManager.AddToken(connection.AccountId, connection.Id);

            var port = AppConfiguration.Instance.StreamNetwork.Port;
            // Real client GM UI (toggle_gm_console / X2Gm / CSGmCommand) keys off enter-world
            // authority + gmFlag — not AAEmu chat "/item". Account access_level>=100 ⇒ GM.
            var accountAccess = accountManager.GetAccountDetails(accountId).AccessLevel;
            if (accountAccess >= 100)
                connection.AddAttribute("gmFlag", true);
            var gm = connection.GetAttribute("gmFlag") != null;

            //   X2EnterWorldResponse (level 5, carries RSA pubKey) -> ChangeState(0) -> SCWorldQueue.
            // SCWorldQueue is what makes the client start sending FinishState packets, which drive the
            // server-side config burst (see FinishStatePacket). After X2EnterWorldResponse the client's
            // encryption gate rejects plain level-1 packets, so EncryptionActive flips on (auto level 5).
            // Prep RSA *before* Encode so WriteKeyParams does not reset SCMessageCount mid-packet
            // (that reused seq 0 → sequence-mv → frozen WASD while TCP stayed up).
            EncryptionManager.Instance.PrepareEnterWorldKeys(connection.Id, connection.AccountId);
            connection.SendPacket(new X2EnterWorldResponsePacket(0, gm, connection.Id, port, connection));
            connection.EncryptionActive = true;
            connection.SendPacket(new ChangeStatePacket(0));
            // at one-second intervals after this handshake edge.
            connection.SendPacket(new PingPacket());
            // After X2EnterWorldResponse(level 5) + ChangeState the 10.0.2.13 client sends FinishState(0),
            // which drives the server-side config burst (see FinishStatePacket). The character list is then
            // pushed by the CSAesXorKeyPacket handler once the client sends its RSA key reply.
        }
        else if (tokenExists)
        {
            Logger.Warn("Token does not match expected account, Token: {0}, AccountId: {1}, Expected AccountId: {2}, IP: {3}", token, accountId, expectedAccountId, connection.Ip);
            connection.Shutdown();
        }
        else
        {
            Logger.Warn("Invalid token during login attempt, Token: {0}, AccountId: {1}, IP: {2}", token, accountId, connection.Ip);
            connection.Shutdown();
        }
    }

    /// <summary>
    /// Start a leave world task
    /// Delay is also calculated
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="leaveWorldTargetType"></param>
    public void Leave(GameConnection connection, LeaveWorldTargetType leaveWorldTargetType)
    {
        switch (leaveWorldTargetType)
        {
            case LeaveWorldTargetType.QuitGame:
            case LeaveWorldTargetType.CharacterSelect:
                if (connection.State == GameState.World)
                {

                    if (connection.LeaveTask != null)
                    {
                        break;
                    }

                    // Say goodbye if player is quitting (but not going to character select)
                    if (leaveWorldTargetType == 0)
                        connection.ActiveChar?.SendMessage(ChatType.System, AppConfiguration.Instance.World.LogoutMessage);

                    var logoutTime = 10000; // in ms

                    // Make it 5 minutes if you're still in combat
                    if (connection.ActiveChar?.IsInBattle ?? false)
                        logoutTime *= 30;

                    // Add 10 minutes if you have a Slave Active
                    if (connection.ActiveChar?.ParentWorld?.SlaveManager.GetActiveSlaveByOwnerObjId(connection.ActiveChar.ObjId) != null)
                        logoutTime += 1000 * 60 * 10;

                    connection.SendPacket(new SCPrepareLeaveWorldPacket(logoutTime, leaveWorldTargetType, false));

                    connection.CancelTokenSource = new CancellationTokenSource();
                    var token = connection.CancelTokenSource.Token;
                    connection.LeaveTask = Task.Run(async () =>
                    {
                        await Task.Delay(logoutTime, token);
                        Instance.LeaveWorldTask(connection, leaveWorldTargetType, connection.ActiveChar);
                    }, token);
                }

                break;
            case LeaveWorldTargetType.ServerSelect:
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
                Logger.Warn($"[Leave] Unknown type: {leaveWorldTargetType}");
                break;
        }
    }

    /// <summary>
    /// Immediately returns an in-world client to character select after a recoverable server-side failure.
    /// for cleanup failures where the connection can no longer be kept consistent.
    /// </summary>
    public bool ReturnToCharacterSelect(GameConnection connection, string reason)
    {
        if (connection == null)
            return false;

        lock (connection)
        {
            if (connection.State != GameState.World || connection.ActiveChar == null)
                return false;

            var activeChar = connection.ActiveChar;
            try
            {
                // Claim the transition before cleanup so duplicate zone-loss notifications cannot run it twice.
                connection.State = GameState.Lobby;

                // A server failure must not wait on, or race, a normal delayed/cancelable logout.
                connection.CancelTokenSource?.Cancel();
                connection.CancelTokenSource?.Dispose();
                connection.CancelTokenSource = null;
                connection.LeaveTask = null;

                Logger.Warn(
                    "Returning {0} (ObjId={1}) to character select: {2}",
                    activeChar.Name, activeChar.ObjId, reason);

                // return-to-lobby path. No SCPrepareLeaveWorld is sent because this is immediate and cannot
                // be canceled by the client.
                LeaveWorldTask(connection, LeaveWorldTargetType.CharacterSelect, activeChar);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(
                    ex,
                    "Could not return {0} (connection {1}) to character select; closing the inconsistent session",
                    activeChar.Name, connection.Id);
                connection.Shutdown();
                return false;
            }
        }
    }

    /// <summary>
    /// Actually leave the game world and return connection to lobby state
    /// Also despawns all owned mounts/pet/vehicles still in the world
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="leaveWorldTarget"></param>
    /// <param name="activeChar"></param>
    public void LeaveWorldTask(GameConnection connection, LeaveWorldTargetType leaveWorldTarget, Character activeChar)
    {
        if (activeChar != null)
        {
            activeChar.DisabledSetPosition = true;
            activeChar.IsOnline = false;
            activeChar.LeaveTime = DateTime.UtcNow;

            // Remove all remaining quest timer tasks
            questManager.RemoveQuestTimer(activeChar.Id, 0);

            // Despawn and unmount everybody from owned Mates
            activeChar.ParentWorld.MateManager.RemoveAndDespawnAllActiveOwnedMates(activeChar);
            activeChar.ParentWorld.SlaveManager.RemoveAndDespawnAllActiveOwnedSlaves(activeChar);

            // Check if still mounted on somebody else's mount and dismount that if needed
            activeChar.ForceDismount(/*AttachUnitReason.PrefabChanged*/); // Dismounting a mount because of unsummoning sends "10" for this

            // Remove from Team (raid/party)
            teamManager.MemberRemoveFromTeam(activeChar, activeChar, RiskyAction.Leave);

            // Remove from all Chat
            chatManager.LeaveAllChannels(activeChar);

            // Handle Family
            if (activeChar.Family > 0)
                familyManager.OnCharacterLogout(activeChar);

            // Handle Guild
            activeChar.Expedition?.OnCharacterLogout(activeChar);

            // Remove player from world (hides and release Id)
            activeChar.Delete();
            // ObjectIdManager.Instance.ReleaseId(activeChar.ObjId);

            // Cancel auto-regen
            //activeChar.StopRegen();

            // Clear Buyback table
            activeChar.BuyBackItems.Wipe();

            // Remove subscribers
            foreach (var subscriber in activeChar.Subscribers)
                subscriber.Dispose();

            // Remove from server
            worldManager.TryRemoveCharacter(activeChar.ObjId);
        }

        GameConnection.SaveAndRemoveFromWorld(activeChar);

        // connection isn't set if we are removing an orphaned Character object
        if (connection != null)
        {
            connection.State = GameState.Lobby;
            connection.LeaveTask = null;
            // Drop the world avatar so re-select does not see a still-“active” character.
            // Session encryption remains active; lobby re-entry republishes the character list.
            connection.ActiveChar = null;
            connection.SendPacket(new SCLeaveWorldGrantedPacket(leaveWorldTarget));
            connection.SendPacket(new ChangeStatePacket(0));
        }
    }
}
