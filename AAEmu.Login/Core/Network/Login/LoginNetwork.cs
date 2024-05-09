using System;
using System.Net;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Login.Core.Packets.C2L;
using AAEmu.Login.Models;
using NLog;

namespace AAEmu.Login.Core.Network.Login;

public class LoginNetwork : Singleton<LoginNetwork>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Server _server;
    private LoginProtocolHandler _handler;

    private LoginNetwork()
    {
        _handler = new LoginProtocolHandler();

        RegisterPacket(CLOffsets.CARequestAuthPacket, typeof(CARequestAuthPacket)); // 0x01 - требует клиент 3.5.1.4 tw
        //RegisterPacket(CLOffsets.CARequestAuthPacket_0x001, typeof(CARequestAuthPacket_0x001));
        //RegisterPacket(CLOffsets.CARequestAuthPacket_0x002, typeof(CARequestAuthPacket_0x002));
        //RegisterPacket(CLOffsets.CARequestAuthPacket_0x003, typeof(CARequestAuthPacket_0x003));
        //RegisterPacket(CLOffsets.CARequestAuthPacket_0x004, typeof(CARequestAuthPacket_0x004));
        RegisterPacket(CLOffsets.CARequestAuthTrionPacket, typeof(CARequestAuthTrionPacket));   // 0x05 - если включен английский язык в лаунчере
        RegisterPacket(CLOffsets.CARequestAuthMailRuPacket, typeof(CARequestAuthMailRuPacket)); // 0x06 - если включен русский язык в лаунчере
        RegisterPacket(CLOffsets.CAChallengeResponsePacket, typeof(CAChallengeResponsePacket));
        RegisterPacket(CLOffsets.CAChallengeResponse2Packet, typeof(CAChallengeResponse2Packet));
        //RegisterPacket(CLOffsets.CAOtpNumberPacket, typeof(CAOtpNumberPacket));
        //RegisterPacket(CLOffsets.CAPcCertNumberPacket, typeof(CAPcCertNumberPacket));
        RegisterPacket(CLOffsets.CAListWorldPacket, typeof(CAListWorldPacket));               // 0x0C
        RegisterPacket(CLOffsets.CAEnterWorldPacket, typeof(CAEnterWorldPacket));             // 0x0D
        RegisterPacket(CLOffsets.CACancelEnterWorldPacket, typeof(CACancelEnterWorldPacket));
        RegisterPacket(CLOffsets.CARequestReconnectPacket, typeof(CARequestReconnectPacket)); // 0x0F
        RegisterPacket(CLOffsets.CARequestAuthTWPacket, typeof(CARequestAuthTWPacket));            // 0x11
        RegisterPacket(CLOffsets.CARequestAuthKakaoPacket, typeof(CARequestAuthKakaoPacket));      // 0x17 - для версий AA 10.8.1.0 kakao

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
        if ((_server != null) && (_server.IsStarted))
            _server.Stop();

        Logger.Info("Network stopped");
    }

    private void RegisterPacket(uint type, Type classType)
    {
        _handler.RegisterPacket(type, classType);
    }
}
