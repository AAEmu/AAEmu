using System.Security.Cryptography;
using System.Text;

namespace AAEmu.Launcher.Core;

/// <summary>
/// Constructs the Trion XML authentication ticket string.
/// </summary>
internal static class TicketBuilder
{
    private const string TicketPrefix = "TFIR"; // "RIFT" backwards (also a Trion game, probably same auth system)
    private const string SignaturePrefix = "Signature: "; // Exactly 11 bytes
    private const string TestSignature = "SIGNATURE_HERE"; // Variable length

    /// <summary>
    /// Builds a version 1 ticket with a SHA256-hashed password.
    /// </summary>
    /// <remarks>
    /// SHA256 hashing without a salt is inherently insecure unless the protocol or transport is encrypted.
    /// </remarks>
    public static string BuildPreHashed(string username, string password)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        var hexHash = Convert.ToHexStringLower(hash);
        return BuildTicket(username, hexHash, version: 1);
    }

    /// <summary>
    /// Builds a version 2 ticket with a plaintext password.
    /// </summary>
    /// <remarks>Plaintext is inherently insecure unless the protocol or transport is encrypted.</remarks>
    public static string BuildPlainText(string username, string password) =>
        BuildTicket(username, password, version: 2);

    /// <summary>
    /// Builds a version 3 ticket with an authentication token.
    /// </summary>
    public static string BuildToken(string username, string token) =>
        BuildTicket(username, token, version: 3);

    private static string BuildTicket(string username, string credential, int version) =>
        TicketPrefix + SignaturePrefix + TestSignature + '\n' +
        $"""
         <?xml version="1.0" encoding="UTF - 8" standalone="yes"?>
         <authTicket version = "1.2">
           <storeToken>1</storeToken>
           <username>{username}</username>
           <password>{credential}</password>
           <version>{version}</version>
         </authTicket>
         """;
}
