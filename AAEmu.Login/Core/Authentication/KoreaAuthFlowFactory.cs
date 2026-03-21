using System.Net;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Services.TwoFactor;
using Microsoft.Extensions.Options;

namespace AAEmu.Login.Core.Authentication;

/// <summary>
/// Factory for creating <see cref="KoreaAuthFlow"/> instances with all required dependencies.
/// </summary>
public class KoreaAuthFlowFactory(
    ILoginController loginController,
    ITwoFactorService twoFactorService,
    IOtpService otpService,
    IPcCertService pcCertService,
    IArsService arsService,
    IOptions<KoreaAuthOptions> options) : IKoreaAuthFlowFactory
{
    public KoreaAuthFlow Create(string username, IPAddress clientIp) =>
        new(
            loginController,
            twoFactorService,
            otpService,
            pcCertService,
            arsService,
            options,
            username,
            clientIp);
}
