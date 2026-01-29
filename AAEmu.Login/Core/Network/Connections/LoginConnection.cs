using System.Collections.Concurrent;
using System.Net;
using System.Text;
using AAEmu.Commons.Models;
using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;
using Microsoft.AspNetCore.Connections;

namespace AAEmu.Login.Core.Network.Connections;

public sealed class LoginConnection : ILoginConnectionOwner
{
    private readonly ConnectionContext _session;
    private readonly ConnectionIdLease _connectionIdLease;
    private readonly ConcurrentDictionary<ushort, ILoginPacketDescriptor> _packets;
    private readonly ILoginProtocolHandler _protocolHandler;
    private readonly SemaphoreSlim _writeLock = new(1);
    private readonly ILogger _logger;

    public ConnectionId Id => _connectionIdLease.Id;
    public IPAddress Ip => _session.RemoteEndPoint is IPEndPoint ipEndPoint ? ipEndPoint.Address : IPAddress.None;
    public InternalConnection? InternalConnection { get; set; }

    public AccountId AccountId { get; set; }
    public string? AccountName { get; set; }
    public DateTime LastLogin { get; set; }
    public IPAddress? LastIp { get; set; }
    public bool IsLocallyConnected { get; }

    public Dictionary<GameServerId, List<LoginCharacterInfo>> Characters { get; }

    /// <summary>
    /// Triggered when the client connection is closed.
    /// </summary>
    public CancellationToken ConnectionClosed => _session.ConnectionClosed;

    public LoginConnection(ConnectionContext session, ConnectionIdLease connectionIdLease,
        ConcurrentDictionary<ushort, ILoginPacketDescriptor> packets, ILoginProtocolHandler protocolHandler,
        ILogger logger)
    {
        _session = session;
        _connectionIdLease = connectionIdLease;
        _packets = packets;
        _protocolHandler = protocolHandler;
        _logger = logger;

        // checks if a connection is from the same machine
        var localIp = ((IPEndPoint?)session.LocalEndPoint)?.Address.ToString() ?? "local";
        var remoteIp = ((IPEndPoint?)session.RemoteEndPoint)?.Address.ToString() ?? "remote";
        IsLocallyConnected = localIp == remoteIp;

        Characters = [];
    }

    public async ValueTask SendPacketAsync(LoginPacket packet, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            packet.EncodeTo(_session.Transport.Output);
            await _session.Transport.Output.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask SendPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await _session.Transport.Output.WriteAsync(packet, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task OnConnectedAsync()
    {
        try
        {
            await RunConnectionAsync();
        }
        catch (Exception ex) when (!IsExpectedDisconnectException(ex))
        {
            _logger.LogError(ex, "Error on LoginConnection {ConnectionID} from {ConnectionIP}", Id, Ip);
        }
        finally
        {
            Shutdown();
            _logger.LogDebug("LoginConnection {ConnectionID} from {ConnectionIP} disconnected", Id, Ip);
        }
    }

    private async Task RunConnectionAsync()
    {
        await DispatchMessagesAsync();
    }

    private async Task DispatchMessagesAsync()
    {
        var input = _session.Transport.Input;

        try
        {
            while (true)
            {
                var result = await input.ReadAsync(CancellationToken.None);
                var buffer = result.Buffer;

                while (_protocolHandler.TryParsePacket(ref buffer, out var packetType, out var packet))
                {
                    // Now that we have the packet type, try to dispatch the packet.
                    if (!_packets.TryGetValue(packetType, out var packetDescriptor))
                    {
                        // If the packet type is unknown, handle the unknown packet.
                        HandleUnknownPacket(packetType, packet);
                    }
                    else
                    {
                        try
                        {
                            // Dispatch the packet for further processing.
                            await packetDescriptor.Dispatch(packet, this, ConnectionClosed);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error on packet dispatch {Type}", packetType);
                        }
                    }
                }

                input.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (IsExpectedDisconnectException(ex))
        {
            // Client disconnected - this is normal, no need to log as error
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Message dispatch error on login connection {ConnectionID} from {ConnectionIP}", Id,
                Ip);
        }
    }

    public void Shutdown()
    {
        if (!_session.ConnectionClosed.IsCancellationRequested)
        {
            _session.Abort();
        }
    }

    public List<LoginCharacterInfo> GetCharacters()
    {
        var res = new List<LoginCharacterInfo>();
        foreach (var characters in Characters.Values)
        {
            res.AddRange(characters);
        }
        return res;
    }

    /// <summary>
    /// Adds the known characters of the account on a specific game server.
    /// </summary>
    /// <param name="gsId">The identifier of the game server.</param>
    /// <param name="characterInfos">The list of characters that the account has on the game server.</param>
    public void AddCharacters(GameServerId gsId, List<LoginCharacterInfo> characterInfos)
    {
        foreach (var character in characterInfos)
            character.GsId = gsId.Value;
        Characters.Add(gsId, characterInfos);
    }

    private void HandleUnknownPacket(uint type, PacketStream stream)
    {
        if (!_logger.IsEnabled(LogLevel.Error))
        {
            return;
        }

        var dump = new StringBuilder();
        for (var i = stream.Pos; i < stream.Count; i++)
            dump.Append($"{stream.Buffer[i]:x2} ");
        _logger.LogError("Unknown packet 0x{Type:x2} from {ConnectionIP}:\n{Dump}", type, Ip, dump);
    }

    private static bool IsExpectedDisconnectException(Exception ex)
    {
        return ex is ConnectionResetException
            or ConnectionAbortedException
            or OperationCanceledException;
    }

    public ValueTask DisposeAsync()
    {
        // _session ConnectionContext is owned by LoginConnectionHandler, so is not disposed here
        _writeLock.Dispose();
        _connectionIdLease.Dispose();
        return ValueTask.CompletedTask;
    }
}
