using AAEmu.Login.Core.Network.Connections;
using Microsoft.AspNetCore.Connections;

namespace AAEmu.Login.Core.Network.Login;

/// <summary>
/// Represents the login endpoint for client connections.
/// </summary>
public class LoginConnectionHandler(
    ILoginConnectionFactory connectionFactory,
    ILoginConnectionTable loginConnectionTable,
    ILogger<LoginConnectionHandler> logger) : ConnectionHandler
{
    public override async Task OnConnectedAsync(ConnectionContext connectionContext)
    {
        logger.LogDebug("Connection from {SessionIP} established, connection ID: {SessionID}",
            connectionContext.RemoteEndPoint,
            connectionContext.ConnectionId);

        await using var connection = connectionFactory.Create(connectionContext);
        try
        {
            loginConnectionTable.AddConnection(connection);
            await connection.OnConnectedAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error on connection from {SessionIP}", connectionContext.RemoteEndPoint);
        }
        finally
        {
            loginConnectionTable.RemoveConnection(connection.Id);
            await connectionContext.DisposeAsync();
        }
    }
}
