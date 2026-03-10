namespace AAEmu.Login.Core.PacketHandlers;

public interface IPacketHandler<in TPacket, in TConnection>
{
    Task Execute(TPacket packet, TConnection connection, CancellationToken cancellationToken);
}
