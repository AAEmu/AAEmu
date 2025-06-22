namespace AAEmu.Login.Core.Network.Login;

public interface ILoginPacket
{
    static abstract ushort TypeId { get; }
}
