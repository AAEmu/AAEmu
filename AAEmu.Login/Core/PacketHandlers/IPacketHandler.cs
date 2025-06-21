namespace AAEmu.Login.Core.PacketHandlers;

public interface IPacketHandler<in TPacket, in TConnection>
{
    void Execute(TPacket packet, TConnection connection);
}
