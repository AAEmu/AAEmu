namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Swaps one of a synthesised item's random attributes for a new roll, with the server choosing
/// which one. This is what the plain 행운의 돌 (luck stone) materials do.
/// </summary>
public class ItemEvolvingReRoll : ItemEvolvingReRollBase
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ItemEvolvingReRoll;

    protected override bool PlayerSelects => false;
}
