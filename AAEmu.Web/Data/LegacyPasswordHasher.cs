using System.Security.Cryptography;
using System.Text;

namespace AAEmu.Web.Data;

/// <summary>
/// Produces password hashes in the format AAEmu.Login calls "legacy": Base64 of the raw SHA-256
/// bytes of the UTF-8 plaintext.
/// </summary>
/// <remarks>
/// This is deliberately the weaker of the two formats AAEmu.Login accepts. It is used here so the
/// web front-end does not need to share code with the login server: <c>PasswordService.IsLegacyFormat</c>
/// recognises the 44-character Base64 form, verifies it against either a plaintext or a hex SHA-256
/// login, and returns <c>SuccessRehashNeeded</c> — which upgrades the stored value to Identity PBKDF2
/// on the account's first real login.
/// <para>
/// Replace this with the shared <c>PasswordService</c> once the hashing code is factored out of
/// AAEmu.Login, so accounts are created with PBKDF2 up front instead of being upgraded later.
/// </para>
/// </remarks>
public static class LegacyPasswordHasher
{
    public static string HashForStorage(string plaintext) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(plaintext)));
}
