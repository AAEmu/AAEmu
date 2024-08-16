using System;

namespace AAEmu.Game.Services.WebApi.Models;

internal record CharacterModel(uint Id, string Name, uint Level, DateTime CreatedAt, bool IsOnline);
