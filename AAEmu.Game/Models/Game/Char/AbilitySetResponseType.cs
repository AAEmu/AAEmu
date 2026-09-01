namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// <c>SCAbilitySetUpdated.responseType</c> values the 10.0.2.13 client maps to notify toasts
/// (<c>saved_job</c> / <c>changed_job</c> / <c>deleted_job</c>). Values ≤ 0 show lack-of-slot.
/// </summary>
public enum AbilitySetResponseType : sbyte
{
    Failed = 0,
    Saved = 1,
    Changed = 2,
    Deleted = 3
}
