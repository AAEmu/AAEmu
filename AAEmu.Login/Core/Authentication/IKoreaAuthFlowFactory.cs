using System.Net;

namespace AAEmu.Login.Core.Authentication;

/// <summary>
/// Factory for creating <see cref="KoreaAuthFlow"/> instances.
/// </summary>
public interface IKoreaAuthFlowFactory
{
    /// <summary>
    /// Creates a new Korea authentication flow.
    /// </summary>
    /// <param name="username">The username of the user being authenticated.</param>
    /// <param name="clientIp">The IP address of the client attempting the authentication.</param>
    /// <returns>A new <see cref="KoreaAuthFlow"/> instance.</returns>
    KoreaAuthFlow Create(string username, IPAddress clientIp);
}
