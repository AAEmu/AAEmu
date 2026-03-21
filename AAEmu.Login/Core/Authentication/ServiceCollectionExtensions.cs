using AAEmu.Login.Core.Services.TwoFactor;

namespace AAEmu.Login.Core.Authentication;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Korea authentication and two-factor authentication services,
    /// including options validation for <see cref="KoreaAuthOptions"/>.
    /// </summary>
    public static void AddKoreaAuth(this IServiceCollection services)
    {
        services
            .AddOptionsWithValidateOnStart<KoreaAuthOptions>()
            .BindConfiguration(KoreaAuthOptions.ConfigurationSectionName)
            .ValidateDataAnnotations();

        services
            .AddSingleton<IKoreaAuthFlowFactory, KoreaAuthFlowFactory>()
            .AddSingleton<ITwoFactorRepository, TwoFactorRepository>()
            .AddSingleton<ITwoFactorService, TwoFactorService>()
            .AddSingleton<IOtpService, OtpService>()
            .AddSingleton<IPcCertService, PcCertService>()
            .AddSingleton<IArsService, ArsService>()
            .AddSingleton(TimeProvider.System);
    }
}
