using AAEmu.Login.Core.PacketHandlers.C2L;

namespace AAEmu.Login.Models;

public readonly record struct LoginResult(bool Success, AccountId AccountId, LoginDeniedReason DenialReason);
