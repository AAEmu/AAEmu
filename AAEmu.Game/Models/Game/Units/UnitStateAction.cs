namespace AAEmu.Game.Models.Game.Units;

/// <summary>
/// no-action sentinel; nonzero states append time, duration, and horizontal/vertical limits.
/// </summary>
public sealed class UnitStateAction
{
    public const int None = 0;

    public int StateType { get; set; }
    public int Time { get; set; }
    public int Duration { get; set; }
    public float MaxHorizontalVelocity { get; set; }
    public float MaxVerticalVelocity { get; set; }
}
