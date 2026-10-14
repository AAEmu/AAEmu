using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Indun.Events;

/// <summary>
/// <c>indun_event_npc_info_broadcastings</c> (13 rows; zone groups 122, 130, 150, 158): a HUD readout of
/// <c>npc_id</c>'s buff <c>buff_id</c>, as a stack count (type 1) or remaining time (type 2,
/// <c>enum_indun_npc_info_broadcasting_types</c>). No row has a start action.
/// </summary>
/// <remarks>
/// A row is a registration, not a trigger: the copy reads its zone group's rows and sends them as
/// SCIndunPlayingInfoBroadcastingPacket (0x2D8) on instance load and on its periodic HUD refresh (see
/// <c>Dungeon.BuildPlayingInfoPacket</c>). A stack is read from the named unit's live buff; a time falls
/// back to the buff's own authored duration counted from the copy's start when no unit carries it.
/// </remarks>
internal class IndunEventNpcInfoBroadcastings : IndunEvent
{
    public uint NpcId { get; set; }
    public uint BuffId { get; set; }
    public byte NpcInfoBroadcastingId { get; set; }

    public override void Subscribe(WorldInstance worldInstance)
    {
        // Nothing to subscribe: the readout is not edge-triggered. The copy sends it from its HUD refresh.
    }
}
