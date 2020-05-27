namespace AAEmu.Commons.Network.Core.Messages
{
    public interface IWritable
    {
        abstract void Write(PacketStream stream);
    }
}
