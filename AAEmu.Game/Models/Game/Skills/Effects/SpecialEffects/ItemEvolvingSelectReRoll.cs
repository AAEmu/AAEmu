namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Swaps a synthesised item's random attribute the player picked, rather than one of the server's
/// choosing. The higher-tier luck stones (기적을 일으키는 행운의 돌) carry this variant.
/// </summary>
public class ItemEvolvingSelectReRoll : ItemEvolvingReRollBase
{
    protected override SpecialType SpecialEffectActionType => SpecialType.ItemEvolvingSelectReRoll;

    protected override bool PlayerSelects => true;
}
