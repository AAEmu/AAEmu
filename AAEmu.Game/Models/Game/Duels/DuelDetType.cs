namespace AAEmu.Game.Models.Game.Duels;

public enum DuelDetType : byte
{
    // det 00=lose, 01=win, 02=surrender (Fled beyond the flag action border), 03=draw
    Lose = 0,
    Win = 1,
    Surrender = 2,
    Draw = 3
}
