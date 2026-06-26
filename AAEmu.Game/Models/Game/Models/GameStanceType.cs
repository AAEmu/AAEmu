namespace AAEmu.Game.Models.Game.Models;

// NOTE: These are the values used in the packets
// In the DB these are referenced as +1 of these
public enum GameStanceType : byte
{
    Combat = 0,
    Relaxed = 1,
    Swim = 2,
    CoSwim = 3,
    ZeroG = 4, // DB game_stances id 5 = STANCE_ZEROG (packet value = DB id - 1)
    Stealth = 5,
    Crouch = 6,
    Prone = 7,
    Fly = 8,
}
