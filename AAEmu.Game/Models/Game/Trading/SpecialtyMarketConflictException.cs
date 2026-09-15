namespace AAEmu.Game.Models.Game.Trading;

public sealed class SpecialtyMarketConflictException() : Exception("The specialty market revision has changed or its singleton row is missing.");
