using Microsoft.AspNetCore.Identity;

namespace AAEmu.Login.Core.Services;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers password hashing and credential verification services.
    /// Configures PBKDF2 with 600,000 iterations per OWASP recommendations.
    /// </summary>
    public static void AddPasswordAuth(this IServiceCollection services)
    {
        services
            .Configure<PasswordHasherOptions>(options => options.IterationCount = 600_000)
            .AddSingleton<IPasswordHasher<string>, PasswordHasher<string>>()
            .AddSingleton<IPasswordService, PasswordService>();
    }
}