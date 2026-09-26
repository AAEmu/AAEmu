namespace AAEmu.Game.Models.Game.World.Zones;

/// <summary>
/// <c>enum_honor_point_war_states</c>: 0..4 trouble_0..trouble_4, 5 battle, 6 war, 7 peace. The same
/// eight values are the client's Lua globals HPWS_TROUBLE_0..4, HPWS_BATTLE, HPWS_WAR and HPWS_PEACE
/// and the <c>hpws</c> byte of SCConflictZoneStatePacket
/// (reader). The order is the escalation order.
/// </summary>
public enum ZoneConflictType : byte
{
    Tension = 0,
    Danger = 1,
    Dispute = 2,
    Unrest = 3,
    Crisis = 4,
    Conflict = 5,
    War = 6,
    Peace = 7
}
