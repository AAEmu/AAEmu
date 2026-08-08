namespace AAEmu.Game.Models.Game;

/// <summary>
/// The <c>extraKind</c> values the 10.0.2.13 client recognises on an
/// <see cref="AccountAttributeKind.AccountBuff"/> attribute as a paid membership.
/// </summary>
/// <remarks>
/// Read out of x2game-dev.dll at rva 0x154f00, which is the whole of the client's membership
/// detection:
/// <code>
/// cmp dword ptr [rdx], 2      ; kind == AccountBuff
/// mov eax, [rdx + 4]          ; extraKind
/// cmp eax, 0x3e9              ; 1001 -> "[PremiumLog] Ancient membership activated, endTime=%llu"
/// cmp eax, 0x3ea              ; 1002 -> "[PremiumLog] Advanced membership activated, endTime=%llu"
/// </code>
/// Nothing else sets those two flags. The premium grade the server sends in SCUpdatePremiumPoint and
/// in UnitState is carried correctly on the wire (both layouts verified) but does not grant membership
/// by itself, which is why an account with grade 6 still read "Patron 0" and the free tier's labor
/// numbers while the attribute list went out empty.
/// </remarks>
public enum AccountMembership : uint
{
    /// <summary>上古会员 - the tier premium_grades attaches buff 7149 to.</summary>
    Ancient = 1001,

    /// <summary>The higher tier the client calls "Advanced membership".</summary>
    Advanced = 1002
}
