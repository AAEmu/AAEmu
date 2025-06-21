using System.Net;
using AAEmu.Commons.Network.Core;
using AAEmu.Login.Core.PacketHandlers;
using AAEmu.Login.Core.Packets.C2L;
using AAEmu.Login.Models;
using NLog;

namespace AAEmu.Login.Core.Network.Login;

public class LoginNetwork : ILoginNetwork
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Server? _server;
    private readonly ILoginProtocolHandler _handler;

    public LoginNetwork(ILoginProtocolHandler protocolHandler,
        ILoginPacketHandler<CARequestAuthPacket> requestAuthPacketHandler,
        ILoginPacketHandler<CARequestAuthTencentPacket> requestAuthTencentPacket,
        ILoginPacketHandler<CARequestAuthGameOnPacket> requestAuthGameOnPacket,
        ILoginPacketHandler<CARequestAuthTrionPacket> requestAuthTrionPacket,
        ILoginPacketHandler<CARequestAuthMailRuPacket> requestAuthMailRuPacket,
        ILoginPacketHandler<CAChallengeResponsePacket> challengeResponsePacket,
        ILoginPacketHandler<CAChallengeResponse2Packet> challengeResponse2Packet,
        ILoginPacketHandler<CAOtpNumberPacket> otpNumberPacket,
        ILoginPacketHandler<CAPcCertNumberPacket> pcCertNumberPacket,
        ILoginPacketHandler<CAListWorldPacket> listWorldPacket,
        ILoginPacketHandler<CAEnterWorldPacket> enterWorldPacket,
        ILoginPacketHandler<CACancelEnterWorldPacket> cancelEnterWorldPacket,
        ILoginPacketHandler<CARequestReconnectPacket> requestReconnectPacket)
    {
        _handler = protocolHandler;

        RegisterPacket(CLOffsets.CARequestAuthPacket, requestAuthPacketHandler);
        RegisterPacket(CLOffsets.CARequestAuthTencentPacket, requestAuthTencentPacket);
        RegisterPacket(CLOffsets.CARequestAuthGameOnPacket, requestAuthGameOnPacket);
        RegisterPacket(CLOffsets.CARequestAuthTrionPacket, requestAuthTrionPacket);
        RegisterPacket(CLOffsets.CARequestAuthMailRuPacket, requestAuthMailRuPacket);
        RegisterPacket(CLOffsets.CAChallengeResponsePacket, challengeResponsePacket);
        RegisterPacket(CLOffsets.CAChallengeResponse2Packet, challengeResponse2Packet);
        RegisterPacket(CLOffsets.CAOtpNumberPacket, otpNumberPacket);
        RegisterPacket(CLOffsets.CAPcCertNumberPacket, pcCertNumberPacket);
        RegisterPacket(CLOffsets.CAListWorldPacket, listWorldPacket);
        RegisterPacket(CLOffsets.CAEnterWorldPacket, enterWorldPacket);
        RegisterPacket(CLOffsets.CACancelEnterWorldPacket, cancelEnterWorldPacket);
        RegisterPacket(CLOffsets.CARequestReconnectPacket, requestReconnectPacket);
    }

    public void Start()
    {
        var config = AppConfiguration.Instance.Network;
        _server = new Server(
            config.Host.Equals("*") ? IPAddress.Any : IPAddress.Parse(config.Host), config.Port, _handler);
        _server.Start();

        Logger.Info("Network started with Number of Connections: " + config.NumConnections);
    }

    public void Stop()
    {
        if (_server is { IsStarted: true })
            _server.Stop();

        Logger.Info("Network stopped");
    }

    private void RegisterPacket<TPacket>(uint type, ILoginPacketHandler<TPacket> packetHandler)
        where TPacket : LoginPacket => _handler.RegisterPacket(type, packetHandler);
}
