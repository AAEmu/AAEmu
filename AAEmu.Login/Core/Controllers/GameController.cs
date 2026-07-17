using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Core.Packets.L2G;
using AAEmu.Login.Models;
using Microsoft.Extensions.Options;

namespace AAEmu.Login.Core.Controllers;

public class GameController(
    IRequestController requestController,
    ILoginConnectionTable connectionTable,
    IOptions<AppConfiguration> appConfig,
    ILogger<GameController> logger)
    : IGameController
{
    private readonly ConcurrentDictionary<GameServerId, GameServer> _gameServers = [];
    private readonly Dictionary<GameServerId, GameServerId> _mirrorsId = [];

    public bool TryGetParentId(GameServerId gsId, out GameServerId id) => _mirrorsId.TryGetValue(gsId, out id);

    private static async Task SendPacketWithDelay(InternalConnection connection, int delay, InternalPacket message)
    {
        await Task.Delay(delay);
        connection.SendPacket(message);
    }

    private string ResolveHostName(string host)
    {
        try
        {
            var parsedHost = Dns.GetHostEntry(host);
            var firstIPv4Address =
                parsedHost.AddressList.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);
            if (firstIPv4Address != null)
            {
                logger.LogDebug("Resolved {Host} to {Address}", host, firstIPv4Address);
                return firstIPv4Address.ToString();
            }

            logger.LogWarning("Unable to resolve {Host} to an IPv4 address", host);
            return host;
        }
        catch (Exception ex)
        {
            // in case of errors, just return it un-parsed
            logger.LogError(ex, "Exception resolving {Host}", host);
            return host;
        }
    }

    public void Load()
    {
        foreach (var gameServerConfig in appConfig.Value.GameServers.Where(gs => !gs.Hidden))
        {
            var id = new GameServerId(gameServerConfig.Id);
            var host = appConfig.Value.SkipHostResolve ? gameServerConfig.Host : ResolveHostName(gameServerConfig.Host);
            var gameServer = new GameServer(id, gameServerConfig.Name, host, gameServerConfig.Port);
            if (!_gameServers.TryAdd(gameServer.Id, gameServer))
            {
                logger.LogError("Game server {ID} ({Name}) has been defined more than once!",
                    gameServer.Id.Value,
                    gameServer.Name);
            }

            var extraInfo = host != gameServerConfig.Host ? "from " + gameServerConfig.Host :
                appConfig.Value.SkipHostResolve ? " (unresolved)" : "";
            logger.LogInformation("Game Server {ID}: {Name} -> {Host}:{Port} {ExtraInfo}", id.Value,
                gameServerConfig.Name, host, gameServerConfig.Port, extraInfo);
        }

        if (_gameServers.IsEmpty)
        {
            logger.LogCritical("No game servers have been defined!");
            return;
        }

        logger.LogInformation("Loaded {Count} game server(s)", _gameServers.Count);
    }

    public void Add(GameServerId gsId, List<GameServerId> mirrorsId, InternalConnection connection)
    {
        if (!_gameServers.TryGetValue(gsId, out var gameServer))
        {
            logger.LogError("GameServer connection from {GameServerIP} is requesting an invalid WorldId {GsId}",
                connection.Ip, gsId);

            Task.Run(() =>
                SendPacketWithDelay(connection, 5000, new LGRegisterGameServerPacket(GSRegisterResult.Error)));
            // connection.SendPacket(new LGRegisterGameServerPacket(GSRegisterResult.Error));
            return;
        }

        gameServer.Connection = connection;
        gameServer.MirrorsId.AddRange(mirrorsId);
        connection.GameServer = gameServer;
        connection.AddAttribute("gsId", gameServer.Id);
        gameServer.SendPacket(new LGRegisterGameServerPacket(GSRegisterResult.Success));

        foreach (var mirrorId in mirrorsId)
        {
            _gameServers[mirrorId].Connection = connection;
            _mirrorsId.Add(mirrorId, gsId);
        }

        logger.LogInformation("Registered GameServer {GameServerId} ({GameServerName}) from {ConnectionIP}",
            gameServer.Id, gameServer.Name, connection.Ip);
    }

    public void Remove(GameServerId gsId)
    {
        if (!_gameServers.TryGetValue(gsId, out var gameServer))
            return;
        gameServer.Connection = null;

        foreach (var mirrorId in gameServer.MirrorsId)
        {
            if (_gameServers.TryGetValue(mirrorId, out var server))
                server.Connection = null;

            _mirrorsId.Remove(mirrorId);
        }

        gameServer.MirrorsId.Clear();
    }

    public async Task<WorldListResult> GetWorldListAsync(ILoginConnection connection)
    {
        var gameServers = _gameServers.Values.ToList();
        if (_gameServers.Values.Any(x => x.Active))
        {
            var (requestIds, creationTask) =
                requestController.Create(gameServers.Count, 20000); // TODO Request 20s
            for (var i = 0; i < gameServers.Count; i++)
            {
                var value = gameServers[i];
                if (!value.Active)
                {
                    requestController.ReleaseId(requestIds[i]);
                    continue;
                }

                var loaded = connection.Characters.ContainsKey(value.Id);
                if (loaded)
                {
                    requestController.ReleaseId(requestIds[i]);
                    continue;
                }

                value.SendPacket(
                    new LGRequestInfoPacket(connection.Id, requestIds[i], connection.AccountId));

            }

            await creationTask;
        }

        return new WorldListResult(gameServers, connection.GetCharacters());
    }

    public void SetLoad(GameServerId gsId, byte load)
    {
        _gameServers[gsId].Load = (GSLoad)load;
    }

    public GameServer? GetGameServer(GameServerId gsId) => _gameServers.GetValueOrDefault(gsId);

    public void RequestEnterWorld(AccountId accountId, ConnectionId connectionId, GameServerId gsId)
    {
        if (!_gameServers.TryGetValue(gsId, out var gs))
        {
            logger.LogWarning("RequestEnterWorld: game server {GsId} not found", gsId);
            return;
        }

        if (!gs.Active)
        {
            logger.LogWarning("RequestEnterWorld: game server {GsId} not active", gsId);
            return;
        }

        gs.SendPacket(new LGPlayerEnterPacket(accountId, connectionId));
    }

    public void RouteEnterWorldResponse(ConnectionId connectionId, GameServerId gsId, byte result)
    {
        var connection = connectionTable.GetConnection(connectionId);
        if (connection is null)
        {
            logger.LogWarning("RouteEnterWorldResponse: connection {ConnectionId} not found", connectionId);
            return;
        }

        var resolvedGsId = gsId;
        if (TryGetParentId(gsId, out var parentId))
        {
            logger.LogDebug(
                "EnterWorld response for mirror game server {MirrorGsId} resolved to parent {ParentGsId} for connection {ConnectionId}",
                gsId, parentId, connectionId);
            resolvedGsId = parentId;
        }

        connection.Session.CompleteEnterWorldRequest(resolvedGsId, result);
    }
}
