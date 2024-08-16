using System;

namespace AAEmu.Game.Services.WebApi.Models;

public record ExpeditionModel(
    uint Id,
    string Name,
    uint Owner,
    string OwnerName,
    uint Mother,
    DateTime CreatedAt,
    uint MemberCount,
    uint OnlineCount);
