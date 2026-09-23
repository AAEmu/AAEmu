using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.GameData;

namespace AAEmu.Game.Models.Account;

/// <summary>Payment method implied by the tier loaded on the connection.</summary>
/// <remarks>
/// The tier itself is content: <c>premium_grades</c> decides which grades are paid (the first grade
/// that carries a buff), so this only compares the loaded grade against that content floor - no
/// literal grade id appears here.
/// </remarks>
public static class AccountTierPaymentRules
{
    public static PaymentMethodType MethodForTier(uint premiumGradeId, uint firstPaidGradeId) =>
        firstPaidGradeId != 0 && premiumGradeId >= firstPaidGradeId
            ? PaymentMethodType.Premium
            : PaymentMethodType.None;
}

/// <summary>
/// Payment, account tier and entitlements for the 10.x connection path. <c>GameConnection.LoadAccount</c>
/// used to carry only "TODO: Load payment and account tier information", so the lobby resolved the tier
/// ad hoc per packet and nothing on the connection ever held the account's entitlements.
/// </summary>
/// <remarks>
/// Runs at the end of <c>LoadAccount</c>, after <c>Characters</c> are filled: the account tier resolves
/// from the best <c>characters.point</c> on the account, and entitlements come from the account-attribute
/// store, which is persisted - so a relog loads the same state again from the database.
/// </remarks>
public static class AccountConnectionState
{
    public static void Load(GameConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var (point, grade) = AccountManager.Instance.GetAccountPremium(connection);
        var entitlements = AccountAttributeManager.Instance.Get(
            connection.AccountId, AppConfiguration.Instance.Id);
        Apply(connection, point, grade, entitlements);
    }

    /// <summary>Pure apply: everything the connection keeps after loading.</summary>
    public static void Apply(
        GameConnection connection,
        int premiumPoint,
        uint premiumGrade,
        IReadOnlyList<AccountAttribute> entitlements)
    {
        ArgumentNullException.ThrowIfNull(connection);
        connection.AccountTier = (premiumPoint, premiumGrade);
        // Payment.Method feeds labor, credit and loyalty. It is not derived here.
        connection.Entitlements = entitlements is null ? [] : entitlements.ToList();
    }
}
