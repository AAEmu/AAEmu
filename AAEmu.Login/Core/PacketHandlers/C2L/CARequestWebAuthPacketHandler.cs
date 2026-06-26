using System.Text.Json;
using AAEmu.Login.Core.Authentication;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CARequestWebAuthPacket"/> sent by clients launched in web/launcher
/// auth mode (passport flow).
/// </summary>
/// <remarks>
/// The client sends its launcher passport as a JSON blob in the <c>auth</c> field, e.g.
/// <c>{"source":"launcher","strUserToken":"...","StrUserName":"test","serverId":"1",...}</c>.
/// AAEmu has no web-auth backend, so the launcher token is trusted: we extract <c>StrUserName</c>
/// and authenticate it through a token-trusted flow (no password, account auto-created).
/// Clients launched normally send <see cref="CARequestAuthPacket"/> instead.
/// </remarks>
public class CARequestWebAuthPacketHandler(ILoginController loginController)
    : ILoginPacketHandler<CARequestWebAuthPacket>
{
    public async Task Execute(CARequestWebAuthPacket packet, ILoginSession session,
        CancellationToken cancellationToken)
    {
        // Resolve the account name from the passport JSON; fall back to the raw value so an
        // unparseable/empty token is denied cleanly (BadAccount) rather than throwing.
        var account = ExtractStrUserName(packet.Auth);
        if (string.IsNullOrEmpty(account))
            account = packet.Auth ?? string.Empty;

        var flow = new TokenAuthFlow(loginController, account, session.Connection.Ip);
        await session.AuthenticateAsync(flow, cancellationToken);
    }

    /// <summary>
    /// Extracts the <c>StrUserName</c> account name from the launcher passport JSON.
    /// Returns an empty string when <paramref name="auth"/> is not the expected JSON object.
    /// </summary>
    private static string ExtractStrUserName(string? auth)
    {
        if (string.IsNullOrWhiteSpace(auth))
            return string.Empty;

        try
        {
            using var doc = JsonDocument.Parse(auth);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("StrUserName", out var name)
                && name.ValueKind == JsonValueKind.String)
            {
                return name.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            // Not JSON — caller falls back to treating the raw value as the account name.
        }

        return string.Empty;
    }
}
