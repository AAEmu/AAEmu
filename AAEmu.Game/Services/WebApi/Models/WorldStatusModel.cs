namespace AAEmu.Game.Services.WebApi.Models;

internal sealed record WorldStatusModel(
    DateTime ServerTimeUtc,
    int UptimeSeconds,
    int PlayerCount,
    IReadOnlyList<WorldPlayerStatusModel> Players,
    IReadOnlyList<WorldZoneConnectionSnapshot> Zones);

internal sealed record WorldPlayerStatusModel(
    uint Id,
    uint ObjectId,
    string Name,
    byte Level,
    uint ZoneKey,
    string ZoneName,
    uint InstanceId,
    float X,
    float Y);
