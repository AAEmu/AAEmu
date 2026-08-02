namespace AAEmu.Game.Models.Game.Char;

/// <summary>
/// Controls whether the character exposes privacy-sensitive information to other players.
/// The client option table uses exactly these two signed-byte values.
/// </summary>
public enum CharacterPrivacyStatus : sbyte
{
    Off = 0,
    On = 1
}
