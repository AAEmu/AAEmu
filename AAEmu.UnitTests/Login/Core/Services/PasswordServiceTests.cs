using AAEmu.Login.Core.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Xunit;

using PasswordVerificationResult = AAEmu.Login.Core.Services.PasswordVerificationResult;

namespace AAEmu.UnitTests.Login.Core.Services;

public class PasswordServiceTests
{
    private readonly PasswordService _sut;
    private readonly PasswordHasher<string> _passwordHasher;

    public PasswordServiceTests()
    {
        var options = Options.Create(new PasswordHasherOptions());
        _passwordHasher = new PasswordHasher<string>(options);
        _sut = new PasswordService(_passwordHasher);
    }

    [Fact]
    public void IsLegacyFormat_Base64Encoded32Bytes_ReturnsTrue()
    {
        // Arrange - 32 bytes encoded as Base64 = 44 characters
        var legacyHash = Convert.ToBase64String(new byte[32]);

        // Act
        var result = _sut.IsLegacyFormat(legacyHash);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsLegacyFormat_Base64Encoded32BytesWithContent_ReturnsTrue()
    {
        // Arrange - real SHA256 hash (32 bytes) as Base64
        var sha256Bytes = new byte[32];
        for (var i = 0; i < 32; i++) sha256Bytes[i] = (byte)i;
        var legacyHash = Convert.ToBase64String(sha256Bytes);

        // Act
        var result = _sut.IsLegacyFormat(legacyHash);

        // Assert
        Assert.True(result);
        Assert.Equal(44, legacyHash.Length);
    }

    [Fact]
    public void IsLegacyFormat_NewFormatHash_ReturnsFalse()
    {
        // Arrange - new format hash from PasswordHasher
        var newHash = _passwordHasher.HashPassword(string.Empty, "test");

        // Act
        var result = _sut.IsLegacyFormat(newHash);

        // Assert
        Assert.False(result);
        Assert.True(newHash.Length >= 84);
    }

    [Fact]
    public void IsLegacyFormat_WrongLength_ReturnsFalse()
    {
        // Arrange - wrong length string
        var wrongLengthHash = "shortstring";

        // Act
        var result = _sut.IsLegacyFormat(wrongLengthHash);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsLegacyFormat_InvalidBase64_ReturnsFalse()
    {
        // Arrange - 44 characters but not valid Base64 that decodes to 32 bytes
        var invalidBase64 = new string('!', 44);

        // Act
        var result = _sut.IsLegacyFormat(invalidBase64);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HashForStorage_Plaintext_ReturnsNewFormatHash()
    {
        // Arrange
        var password = Password.FromPlaintext("mypassword123");

        // Act
        var hash = _sut.HashForStorage(password);

        // Assert
        Assert.False(_sut.IsLegacyFormat(hash));
        Assert.True(hash.Length >= 84);
    }

    [Fact]
    public void HashForStorage_Sha256Hex_ReturnsLegacyBase64Format()
    {
        // Arrange - 32 bytes as a hex string (legacy format)
        var passwordBytes = new byte[32];
        for (var i = 0; i < 32; i++) passwordBytes[i] = (byte)i;
        var hex = Convert.ToHexString(passwordBytes);
        var expectedBase64 = Convert.ToBase64String(passwordBytes);

        // Act
        var hash = _sut.HashForStorage(Password.FromSha256Hex(hex));

        // Assert - must be the legacy Base64 format, NOT a PBKDF2 hash
        Assert.Equal(expectedBase64, hash);
        Assert.True(_sut.IsLegacyFormat(hash));
    }

    [Fact]
    public void HashForStorage_Sha256Hex_VerifiableByPlaintextViaLegacy()
    {
        // Regression test for the double-hash bug:
        // When CreateAndLoginInvalid is called with a SHA256-hex password,
        // HashForStorage must store the legacy Base64 format so a subsequent plaintext login can verify it.
        var passwordBytes = new byte[32];
        for (var i = 0; i < 32; i++) passwordBytes[i] = (byte)i;
        var hex = Convert.ToHexString(passwordBytes);

        // Create account from SHA256-hex — password stored as legacy Base64
        var storedHash = _sut.HashForStorage(Password.FromSha256Hex(hex));

        // Verify — should succeed (SuccessRehashNeeded because it's legacy format)
        var resultPreHashed = _sut.VerifyPassword(storedHash, Password.FromSha256Hex(hex));
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, resultPreHashed);
    }

    [Fact]
    public void VerifyPassword_LegacyFormat_CorrectPassword_ReturnsSuccessRehashNeeded()
    {
        // Arrange - legacy format stores Base64(raw bytes), client sends hex string
        var passwordBytes = new byte[32];
        for (var i = 0; i < 32; i++) passwordBytes[i] = (byte)i;
        var legacyHash = Convert.ToBase64String(passwordBytes);
        var passwordHex = Convert.ToHexString(passwordBytes);

        // Act
        var result = _sut.VerifyPassword(legacyHash, Password.FromSha256Hex(passwordHex));

        // Assert
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, result);
    }

    [Fact]
    public void VerifyPassword_LegacyFormat_WrongPassword_ReturnsFailed()
    {
        // Arrange
        var storedPasswordBytes = new byte[32];
        for (var i = 0; i < 32; i++) storedPasswordBytes[i] = (byte)i;
        var legacyHash = Convert.ToBase64String(storedPasswordBytes);

        var wrongPasswordBytes = new byte[32];
        for (var i = 0; i < 32; i++) wrongPasswordBytes[i] = (byte)(i + 1);
        var wrongPasswordHex = Convert.ToHexString(wrongPasswordBytes);

        // Act
        var result = _sut.VerifyPassword(legacyHash, Password.FromSha256Hex(wrongPasswordHex));

        // Assert
        Assert.Equal(PasswordVerificationResult.Failed, result);
    }

    [Fact]
    public void VerifyPassword_NewFormat_CorrectPassword_ReturnsSuccess()
    {
        // Arrange
        var passwordHex = "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F";
        var newHash = _sut.HashForStorage(Password.FromPlaintext(passwordHex));

        // Act
        var result = _sut.VerifyPassword(newHash, Password.FromPlaintext(passwordHex));

        // Assert
        Assert.Equal(PasswordVerificationResult.Success, result);
    }

    [Fact]
    public void VerifyPassword_NewFormat_WrongPassword_ReturnsFailed()
    {
        // Arrange
        var passwordHex = "000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F";
        var newHash = _sut.HashForStorage(Password.FromPlaintext(passwordHex));

        var wrongPasswordHex = "0102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F20";

        // Act
        var result = _sut.VerifyPassword(newHash, Password.FromPlaintext(wrongPasswordHex));

        // Assert
        Assert.Equal(PasswordVerificationResult.Failed, result);
    }

    [Fact]
    public void VerifyPassword_MigrationScenario_PreHashed_Works()
    {
        // Arrange - simulate a user with legacy password logging in with pre-hashed password
        var passwordBytes = new byte[32];
        for (var i = 0; i < 32; i++) passwordBytes[i] = (byte)i;
        var legacyHash = Convert.ToBase64String(passwordBytes);
        var passwordHex = Convert.ToHexString(passwordBytes);

        // Act - first login with legacy format (pre-hashed, returns SuccessRehashNeeded)
        var firstResult = _sut.VerifyPassword(legacyHash, Password.FromSha256Hex(passwordHex));

        // Simulate rehashing with plaintext equivalent
        var newHash = _sut.HashForStorage(Password.FromPlaintext(passwordHex));

        // Second login with new format
        var secondResult = _sut.VerifyPassword(newHash, Password.FromPlaintext(passwordHex));

        // Assert
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, firstResult);
        Assert.Equal(PasswordVerificationResult.Success, secondResult);
    }

    [Fact]
    public void VerifyPassword_LegacyFormat_Plaintext_CorrectPassword_ReturnsSuccessRehashNeeded()
    {
        // Arrange - legacy format stores Base64(SHA256(plaintext)), new client sends plaintext
        var plaintext = "mypassword123";
        var sha256Bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plaintext));
        var legacyHash = Convert.ToBase64String(sha256Bytes);

        // Act
        var result = _sut.VerifyPassword(legacyHash, Password.FromPlaintext(plaintext));

        // Assert
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, result);
    }

    [Fact]
    public void VerifyPassword_LegacyFormat_Plaintext_WrongPassword_ReturnsFailed()
    {
        // Arrange
        var plaintext = "mypassword123";
        var sha256Bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plaintext));
        var legacyHash = Convert.ToBase64String(sha256Bytes);

        // Act
        var result = _sut.VerifyPassword(legacyHash, Password.FromPlaintext("wrongpassword"));

        // Assert
        Assert.Equal(PasswordVerificationResult.Failed, result);
    }

    [Fact]
    public void VerifyPassword_MigrationScenario_Plaintext_Works()
    {
        // Arrange - simulate plaintext login against legacy hash, rehash, then verify again
        var plaintext = "mypassword123";
        var sha256Bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plaintext));
        var legacyHash = Convert.ToBase64String(sha256Bytes);

        // Act - first login with plaintext against legacy format
        var firstResult = _sut.VerifyPassword(legacyHash, Password.FromPlaintext(plaintext));

        // Simulate rehashing with plaintext
        var newHash = _sut.HashForStorage(Password.FromPlaintext(plaintext));

        // Second login with new format
        var secondResult = _sut.VerifyPassword(newHash, Password.FromPlaintext(plaintext));

        // Assert
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, firstResult);
        Assert.Equal(PasswordVerificationResult.Success, secondResult);
    }
}
