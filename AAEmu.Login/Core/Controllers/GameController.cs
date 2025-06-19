using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Core.Packets.L2C;
using AAEmu.Login.Core.Packets.L2G;
using AAEmu.Login.Models;
using NLog;

namespace AAEmu.Login.Core.Controllers;

public class GameController : Singleton<GameController>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private readonly ConcurrentDictionary<GameServerId, GameServer> _gameServers = [];
    private readonly Dictionary<GameServerId, GameServerId> _mirrorsId = [];

    public bool TryGetParentId(GameServerId gsId, out GameServerId id) => _mirrorsId.TryGetValue(gsId, out id);

    private static async Task SendPacketWithDelay(InternalConnection connection, int delay, InternalPacket message)
    {
        await Task.Delay(delay);
        connection.SendPacket(message);
    }

    private static string ResolveHostName(string host)
    {
        try
        {
            var parsedHost = Dns.GetHostEntry(host);
            var firstIPv4Address =
                parsedHost.AddressList.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);
            if (firstIPv4Address != null)
            {
                Logger.Debug($"Resolved {host} to {firstIPv4Address}");
                return firstIPv4Address.ToString();
            }
            Logger.Warn($"Unable to resolved {host}");
            return host;
        }
        catch (Exception e)
        {
            // in case of errors, just return it un-parsed
            Logger.Error(e, $"Exception resolving {host}: {e.Message}");
            return host;
        }
    }

    public void Load()
    {
        using var connection = MySQL.CreateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM game_servers WHERE hidden = 0";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = new GameServerId(reader.GetByte("id"));
            var name = reader.GetString("name");
            var loadedHost = reader.GetString("host");
            var host = AppConfiguration.Instance.SkipHostResolve ? loadedHost : ResolveHostName(loadedHost);
            var port = reader.GetUInt16("port");
            var gameServer = new GameServer(id, name, host, port);
            if (!_gameServers.TryAdd(gameServer.Id, gameServer))
            {
                Logger.Error("Game Server {id} ({name}) already exists in the game_servers table!", gameServer.Id.Value, gameServer.Name);
            }

            var extraInfo = host != loadedHost ? "from " + loadedHost :
                AppConfiguration.Instance.SkipHostResolve ? " (unresolved)" : "";
            Logger.Info($"Game Server {id.Value}: {name} -> {host}:{port} {extraInfo}");
        }

        if (_gameServers.IsEmpty)
        {
            Logger.Fatal("No servers have been defined in the game_servers table!");
            return;
        }

        Logger.Info($"Loaded {_gameServers.Count} game server(s)");
    }

    public void Add(GameServerId gsId, List<GameServerId> mirrorsId, InternalConnection connection)
    {
        if (!_gameServers.TryGetValue(gsId, out var gameServer))
        {
            Logger.Error($"GameServer connection from {connection.Ip} is requesting an invalid WorldId {gsId}");

            Task.Run(() => SendPacketWithDelay(connection, 5000, new LGRegisterGameServerPacket(GSRegisterResult.Error)));
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
        Logger.Info($"Registered GameServer {gameServer.Id} ({gameServer.Name}) from {connection.Ip}");
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

    public async Task RequestWorldListAsync(LoginConnection connection)
    {
        var gameServers = _gameServers.Values.ToList();
        if (_gameServers.Values.Any(x => x.Active))
        {
            var (requestIds, creationTask) =
                RequestController.Instance.Create(gameServers.Count, 20000); // TODO Request 20s
            for (var i = 0; i < gameServers.Count; i++)
            {
                var value = gameServers[i];
                if (!value.Active)
                {
                    RequestController.Instance.ReleaseId(requestIds[i]);
                    continue;
                }

                var loaded = connection.Characters.ContainsKey(value.Id);
                if (loaded)
                {
                    RequestController.Instance.ReleaseId(requestIds[i]);
                    continue;
                }

                value.SendPacket(
                       new LGRequestInfoPacket(connection.Id, requestIds[i], connection.AccountId));

            }

            await creationTask;
        }
        connection.SendPacket(new ACWorldListPacket(gameServers, connection.GetCharacters()));
    }

    public void SetLoad(GameServerId gsId, byte load)
    {
        _gameServers[gsId].Load = (GSLoad)load;
    }

    public void RequestEnterWorld(LoginConnection connection, GameServerId gsId)
    {
        if (!_gameServers.TryGetValue(gsId, out var gs))
            return;
        if (!gs.Active)
            return;
        gs.SendPacket(new LGPlayerEnterPacket(connection.AccountId, connection.Id));
    }

    public void EnterWorld(LoginConnection connection, GameServerId gsId, byte result)
    {
        switch (result)
        {
            case 0 when _gameServers.TryGetValue(gsId, out var server):
                connection.SendPacket(new ACWorldCookiePacket(connection, server));
                break;
            case 0:
                // TODO ...
                break;
            case 1:
                connection.SendPacket(new ACEnterWorldDeniedPacket(0)); // TODO change reason
                break;
            default:
                // TODO ...
                break;
        }
    }
}
