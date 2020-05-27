namespace AAEmu.Commons.Network.Core.Messages
{
    public interface IHandler
    {
        abstract void Handler(Session session, IReadable message);
    }
}
