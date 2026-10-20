namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Gates premature application of a buff named as another buff's <c>transform_buff_id</c>.
/// </summary>
/// <remarks>
/// Soulslake weapons (and tension → line-broken) stack a Multiple family whose
/// <c>transform_buff_id</c> is the unlocked form. The same 1 s ICD <c>started</c> row also
/// triggers that unlocked buff directly, so without this gate Abyssal Power lands on the first
/// hit instead of when the stack hits its ceiling.
/// Once the transform target is already live, later ICD triggers must still refresh it.
/// </remarks>
public static class BuffTransformDeferRules
{
    /// <param name="incomingBuffId">Buff about to be applied.</param>
    /// <param name="alreadyHasIncoming">True when the target already carries that buff.</param>
    /// <param name="hasSourceBelowCeiling">
    /// True when a live buff names <paramref name="incomingBuffId"/> as its transform target
    /// and has not yet reached its own <c>max_stack</c>.
    /// </param>
    public static bool ShouldDefer(
        uint incomingBuffId,
        bool alreadyHasIncoming,
        bool hasSourceBelowCeiling) =>
        incomingBuffId != 0 && !alreadyHasIncoming && hasSourceBelowCeiling;
}
