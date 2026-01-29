namespace AAEmu.Login.Core.Network.Connections;

public interface ILoginSessionFactory
{
    ILoginSession Create(ILoginConnection connection);
}
