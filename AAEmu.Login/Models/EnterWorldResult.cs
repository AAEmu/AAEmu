namespace AAEmu.Login.Models;

public readonly record struct EnterWorldResult(bool Success, GameServer? Server, byte DenialReason);
