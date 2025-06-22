using System.Net;
using AAEmu.Commons.Models;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Network.Connections;

public class LoginConnection
{
    private readonly ISession _session;

    public ConnectionId Id => new(_session.SessionId);
    public IPAddress Ip => _session.Ip;
    public InternalConnection? InternalConnection { get; set; }
    public PacketStream? LastPacket { get; set; }

    public AccountId AccountId { get; set; }
    public string? AccountName { get; set; }
    public DateTime LastLogin { get; set; }
    public IPAddress? LastIp { get; set; }
    public bool IsLocallyConnected { get; private set; }

    public Dictionary<GameServerId, List<LoginCharacterInfo>> Characters { get; }

    public LoginConnection(ISession session)
    {
        _session = session;

        // checks if a connection is from the same machine
        var localIp = session?.Socket?.LocalEndPoint?.ToString() ?? "local:0";
        var remoteIp = session?.Socket?.RemoteEndPoint?.ToString() ?? "remote:0";
        localIp = localIp[..localIp.IndexOf(':')];
        remoteIp = remoteIp[..remoteIp.IndexOf(':')];
        IsLocallyConnected = localIp == remoteIp;

        Characters = [];
    }

    public void SendPacket(LoginPacket packet)
    {
        SendPacket(packet.Encode());
    }

    public void SendPacket(byte[] packet)
    {
        _session.SendPacket(packet);
    }

    public static void OnConnect()
    {
    }

    public void Shutdown()
    {
        _session.Close();
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

    public void AddCharacters(GameServerId gsId, List<LoginCharacterInfo> characterInfos)
    {
        foreach (var character in characterInfos)
            character.GsId = gsId.Value;
        Characters.Add(gsId, characterInfos);
    }
}
