namespace AAEmu.Game.Models.StaticValues;

// Any value not in the enum results in the game client saying the name is pending deletion
public enum CharacterCreateError : byte
{
    Ok = 0,
    Failed = 3, // Also generates Name is pending deletion
    NameAlreadyExists = 4,
    InvalidCharacters = 5,
}
