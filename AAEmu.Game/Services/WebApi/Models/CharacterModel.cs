namespace AAEmu.Game.Services.WebApi.Models;

internal record CharacterModel(uint Id, string Name, uint Level, DateTime CreatedAt, bool IsOnline,
    uint FamilyId = 0, uint ExpeditionId = 0);
