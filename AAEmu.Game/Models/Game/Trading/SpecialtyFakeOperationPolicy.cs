namespace AAEmu.Game.Models.Game.Trading;

/// <summary>Developer-only specialty operations carried by the client packet family.</summary>
public enum SpecialtyFakeOperationKind : byte
{
    Buy = 0,
    Sell = 1
}

/// <summary>Why a fake specialty request is refused. Neither outcome mutates market state.</summary>
public enum SpecialtyFakeOperationDecision : byte
{
    RejectMalformed = 0,
    RejectUnsupported = 1
}

/// <summary>
/// The fake specialty packets carry only an item type and a count. The retail flow has no
/// authoritative market mutation for them, so the server refuses them rather than inventing a
/// price, cargo change, or payment.
/// </summary>
public static class SpecialtyFakeOperationPolicy
{
    public static SpecialtyFakeOperationDecision Evaluate(uint type, int itemCount)
    {
        return type == 0 || itemCount <= 0
            ? SpecialtyFakeOperationDecision.RejectMalformed
            : SpecialtyFakeOperationDecision.RejectUnsupported;
    }
}
