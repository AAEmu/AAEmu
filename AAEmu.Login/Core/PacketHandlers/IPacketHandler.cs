namespace AAEmu.Login.Core.PacketHandlers;

public interface IPacketHandler<in TPacket, in TConnection>
{
    Task ExecuteAsync(TPacket packet, TConnection connection);
}
