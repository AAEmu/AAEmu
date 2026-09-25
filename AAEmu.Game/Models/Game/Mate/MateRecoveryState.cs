namespace AAEmu.Game.Models.Game.Mate;

/// <summary>
/// The three recovery values authored on a summonable mate NPC, loaded from compact content by
/// <c>MateGameData</c> and carried on the summoned <c>Mate</c>. Nothing recovery-related is
/// persisted: the values are re-read from content on every summon, so the persisted form and its
/// reader belong to the C09B effect that will actually consume them.
/// </summary>
public readonly record struct MateRecoveryState(
    int MateReviveDelay,
    int MateReviveHpPercent,
    int MateReviveMpPercent);
