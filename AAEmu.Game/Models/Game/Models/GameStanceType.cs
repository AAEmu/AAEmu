namespace AAEmu.Game.Models.Game.Models;

public enum GameStanceType : byte
{
    Normal = 1, // actually called combat in the DB
    Relaxed = 2,
    Swim = 3,
    CoSwim = 4,
    Combat = 5,
    Stealth = 6,
    Crouch = 7,
    Prone = 8,
    Fly = 9,
}
