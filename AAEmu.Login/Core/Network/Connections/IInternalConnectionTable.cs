namespace AAEmu.Login.Core.Network.Connections;

public interface IInternalConnectionTable
{
    void AddConnection(InternalConnection con);
    InternalConnection? GetConnection(uint id);
    InternalConnection? RemoveConnection(uint id);
}
