using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Indun.Actions;

/// <summary>
/// <c>indun_action_round_alarms</c> (8 rows): <c>round_alarm_kind_id</c> 1 starts the current round and 2
/// ends it (<c>enum_indun_round_alarm_kinds</c>); <c>show_ui</c> is 'f' on one row only (action 334, zone
/// group 130, "no UI, operations log"). Each alarm is one SCIndunRoundPlayStatusPacket to every player
/// in the copy.
/// </summary>
internal class IndunActionRoundAlarm : IndunAction
{
    public byte RoundAlarmKindId { get; set; }
    public bool ShowUi { get; set; }

    public override void Execute(WorldInstance worldInstance)
    {
        var dungeon = worldInstance?.DungeonInstance;
        if (dungeon == null)
        {
            Logger.Debug($"IndunActionRoundAlarm {Id}: world {worldInstance?.Id} is not a dungeon copy");
            return;
        }

        dungeon.RoundAlarm(RoundAlarmKindId, ShowUi);
    }
}
