namespace AAEmu.Login.Core.Network.Internal;

public interface IInternalPacket
{
    static abstract ushort TypeId { get; }
}
