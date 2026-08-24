namespace AAEmu.World.Core.Zone;

/// <summary>
/// One Zone host process: zone key plus the instance id from join (<c>ZWJoin.iid</c>).
/// Continent hosts use instance id 0. Dungeon copies of the same zone use distinct ids.
/// </summary>
public readonly record struct ZoneInstanceKey(uint ZoneId, uint InstanceId);
