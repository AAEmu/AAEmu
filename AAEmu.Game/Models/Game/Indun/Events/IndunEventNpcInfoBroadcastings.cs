using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Indun.Events;

/// <summary>
/// <c>indun_event_npc_info_broadcastings</c> (13 rows; zone groups 122, 130, 150, 158): a HUD readout of
/// <c>npc_id</c>'s buff <c>buff_id</c>, as a stack count (type 1) or remaining time (type 2,
/// <c>enum_indun_npc_info_broadcasting_types</c>). No row has a start action. The client shows it through
/// SCIndunPlayingInfoBroadcastingPacket (0x2D8), whose body is an i64 "zi" plus a length-prefixed inner
/// packet (x2game-dev.dll 0x39c63190 and 0x39d6e0d0) of unknown layout, so nothing is sent: the event
/// loads and stays inert.
/// </summary>
internal class IndunEventNpcInfoBroadcastings : IndunEvent
{
    public uint NpcId { get; set; }
    public uint BuffId { get; set; }
    public byte NpcInfoBroadcastingId { get; set; }

    public override void Subscribe(WorldInstance worldInstance)
    {
        Logger.Debug($"IndunEventNpcInfoBroadcasting {Id}: HUD readout of npc {NpcId} buff {BuffId} type {NpcInfoBroadcastingId} is not sent (inner packet layout unknown), world {worldInstance?.Id}");
    }
}
