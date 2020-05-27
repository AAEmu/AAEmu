namespace AAEmu.Commons.Network.Core.Messages
{
    public interface IReadable
    {
        abstract void Read(PacketStream stream);
    }
}
