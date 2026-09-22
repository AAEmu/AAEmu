using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Indun.Actions;

/// <summary>
/// <c>indun_action_next_rounds</c> (8 rows, zone groups 125, 126, 130): moves the copy's round counter by
/// <c>round_add</c>. The counter, the clamp and the once-only completion live in <see cref="IndunRoundState"/>.
/// </summary>
internal class IndunActionNextRound : IndunAction
{
    public int RoundAdd { get; set; }

    public override void Execute(WorldInstance worldInstance)
    {
        var dungeon = worldInstance?.DungeonInstance;
        if (dungeon == null)
        {
            Logger.Debug($"IndunActionNextRound {Id}: world {worldInstance?.Id} is not a dungeon copy");
            return;
        }

        dungeon.ApplyNextRound(RoundAdd);
    }
}
