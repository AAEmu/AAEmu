namespace AAEmu.UnitTests;

/// <summary>
/// Renders packet bodies for order-sensitive comparison.
/// </summary>
/// <remarks>
/// TUnit's <c>IsEquivalentTo</c> on a <c>byte[]</c> is order-insensitive and its <c>IsEqualTo</c>
/// compares references, so neither can assert a wire layout. Comparing the hex text pins the exact
/// bytes in the exact order.
/// </remarks>
public static class Hex
{
    public static string Of(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
