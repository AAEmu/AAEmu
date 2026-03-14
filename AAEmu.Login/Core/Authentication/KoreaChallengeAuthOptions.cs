using System.ComponentModel.DataAnnotations;

namespace AAEmu.Login.Core.Authentication;

/// <summary>
/// Configuration for Korea challenge-response authentication (V2, AES-256 with sha256_crypt key).
/// </summary>
/// <remarks>
/// <para><b>Security trade-off:</b> sha256_crypt at 100,000 rounds is approximately 3× weaker than
/// PBKDF2@310k (ASP.NET Core Identity default) for offline attacks against a stolen DB.
/// At 300,000 rounds it reaches approximate parity. The hash is never transmitted to clients —
/// only the salt and round count are sent in the challenge packet.</para>
/// <para>V1 (<c>ACChallengePacket</c> / SHA-1 key) is intentionally not configurable.
/// SHA-1 without a per-account salt is ~17 million times weaker than PBKDF2@600k.</para>
/// </remarks>
public class KoreaChallengeAuthOptions
{
    public const string ConfigurationSectionName = "KoreaChallengeAuth";

    /// <summary>
    /// Whether Korea challenge-response authentication is enabled.
    /// When <c>false</c> (default), <c>korea_challenge_hash</c> is never written to the DB
    /// and clients using Korea auth cannot log in.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Number of sha256_crypt rounds for newly computed Korea auth challenge hashes.
    /// Valid range: 1,000–999,999,999 (sha256_crypt spec limit).
    /// Higher rounds increase offline-attack resistance but also increase the client's
    /// login computation time on low-end hardware.
    /// Rounds are stored in the hash string; existing accounts keep their original rounds
    /// until they next log in by plaintext password.
    /// </summary>
    [Range(1_000, 999_999_999)]
    public int Rounds { get; set; } = 100_000;
}
