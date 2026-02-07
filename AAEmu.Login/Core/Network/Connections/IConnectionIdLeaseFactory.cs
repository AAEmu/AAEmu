namespace AAEmu.Login.Core.Network.Connections;

public interface IConnectionIdLeaseFactory
{
    /// <summary>
    /// Rents a new connection ID, returning a lease that will release the ID when disposed.
    /// </summary>
    /// <returns>A leased connection ID.</returns>
    /// <exception cref="InvalidOperationException">The available identifiers have been exhausted.</exception>
    ConnectionIdLease Rent();
}
