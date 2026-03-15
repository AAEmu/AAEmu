using AAEmu.Login.Core.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AAEmu.UnitTests.Login.Core.Services;

/// <summary>
/// Not a correctness test — run manually to generate SQL seed rows for Korea auth testing.
/// Output appears in the test runner's stdout.
/// </summary>
public class KoreaSeedDataGeneratorTests
{
    private readonly PasswordService _passwordService;

    public KoreaSeedDataGeneratorTests()
    {
        var options = Options.Create(new PasswordHasherOptions());
        _passwordService = new PasswordService(new PasswordHasher<string>(options));
    }

    [Test]
    public void GenerateKoreaTestAccount()
    {
        const string Username = "KrTest";
        const string TestPassword = "KrTesting";
        const int Rounds = 100_000;

        var pbkdf2Hash = _passwordService.HashForStorage(Password.FromPlaintext(TestPassword));
        var koreaHash = _passwordService.ComputeKoreaChallengeHash(TestPassword, rounds: Rounds);

        Console.WriteLine($"-- Korea test account: username={Username}, password={TestPassword}");
        Console.WriteLine($"-- PBKDF2:             {pbkdf2Hash}");
        Console.WriteLine($"-- korea_challenge_hash: {koreaHash}");
        Console.WriteLine();
        Console.WriteLine(
            $"INSERT INTO `users` (username, password, korea_challenge_hash, email, last_ip, last_login, created_at, updated_at)" +
            $" VALUES ('{Username}', '{pbkdf2Hash}', '{koreaHash}', '', '127.0.0.1', 0, 0, 0);");
    }
}
