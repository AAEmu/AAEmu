namespace AAEmu.Login.Models;

/// <summary>
/// Korea challenge-response authentication material for a specific account.
/// Returned by <see cref="AAEmu.Login.Core.Controllers.ILoginController.GetKoreaAuthInfoAsync"/>.
/// </summary>
/// <param name="AccountId">The account's numeric identifier.</param>
/// <param name="ChallengeKeyHash">
/// The 32-byte raw sha256_crypt hash used as the AES-256 key during V2 verification.
/// Do not transmit or log this value.
/// </param>
/// <param name="ChallengeSalt">
/// The salt extracted from the stored <c>$5$</c> hash string.
/// Sent to the client in <c>ACChallenge2Packet</c> so the client can derive the same key.
/// </param>
/// <param name="ChallengeRounds">
/// The iteration count extracted from the stored <c>$5$</c> hash string.
/// Sent to the client in <c>ACChallenge2Packet</c> so the client knows how many rounds to apply.
/// </param>
public record KoreaAuthInfo(
    AccountId AccountId,
    ReadOnlyMemory<byte> ChallengeKeyHash,
    string ChallengeSalt,
    int ChallengeRounds
);
