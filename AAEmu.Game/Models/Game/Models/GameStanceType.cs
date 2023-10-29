namespace AAEmu.Game.Models.Game.Models;

public enum GameStanceType : byte
{
    Combat = 1,
    Relaxed = 2,
    Swim = 3,
    CoSwim = 4,
    Combat2 = 5, // actually also called combat in the DB
    Stealth = 6,
    Crouch = 7,
    Prone = 8,
    Fly = 9,
}
