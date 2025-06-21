using System.Net;
using AAEmu.Commons.Network.Core;
using AAEmu.Login.Core.PacketHandlers;
using AAEmu.Login.Core.Packets.G2L;
using AAEmu.Login.Models;
using NLog;

namespace AAEmu.Login.Core.Network.Internal;

public class InternalNetwork : IInternalNetwork
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Server? _server;
    private readonly IInternalProtocolHandler _handler;

    public InternalNetwork(IInternalProtocolHandler protocolHandler,
        IInternalPacketHandler<GLRegisterGameServerPacket> registerGameServerPacketHandler,
        IInternalPacketHandler<GLPlayerEnterPacket> playerEnterPacketHandler,
        IInternalPacketHandler<GLPlayerReconnectPacket> playerReconnectPacketHandler,
        IInternalPacketHandler<GLRequestInfoPacket> requestInfoPacketHandler,
        IInternalPacketHandler<GLGameServerLoadPacket> gameServerLoadPacketHandler)
    {
        _handler = protocolHandler;

        RegisterPacket(GLOffsets.GLRegisterGameServerPacket, registerGameServerPacketHandler);
        RegisterPacket(GLOffsets.GLPlayerEnterPacket, playerEnterPacketHandler);
        RegisterPacket(GLOffsets.GLPlayerReconnectPacket, playerReconnectPacketHandler);
        RegisterPacket(GLOffsets.GLRequestInfoPacket, requestInfoPacketHandler);
        RegisterPacket(GLOffsets.GLGameServerLoadPacket, gameServerLoadPacketHandler);
    }

    public void Start()
    {
        var config = AppConfiguration.Instance.InternalNetwork;
        var host =
            new IPEndPoint(config.Host.Equals("*") ? IPAddress.Any : IPAddress.Parse(config.Host), config.Port);

        _server = new Server(host.Address, host.Port, _handler);
        _server.Start();

        Logger.Info("InternalNetwork started");
    }

    public void Stop()
    {
        if (_server?.IsStarted == true)
            _server.Stop();

        Logger.Info("InternalNetwork stoped");
    }

    private void RegisterPacket<TPacket>(uint type, IInternalPacketHandler<TPacket> packetHandler)
        where TPacket : InternalPacket => _handler.RegisterPacket(type, packetHandler);
}
