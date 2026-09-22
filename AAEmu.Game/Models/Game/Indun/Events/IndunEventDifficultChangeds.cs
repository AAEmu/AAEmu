using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Indun.Events;

/// <summary>
/// <c>indun_event_difficult_changeds</c> (1 row: zone group 146, <c>min_difficult</c> 0 .. <c>max_difficult</c>
/// 12, event 309 "난이도 선택됨" to action 363 "난이도 선택으로 자물쇠 열림", a doodad phase change that
/// opens the lock). Fires when the copy's difficulty is set inside the range, from the H-window pick
/// (CSSelectInstanceDifficultPacket) that <see cref="Dungeon.SetDifficult"/> applies to the copy.
/// </summary>
internal class IndunEventDifficultChangeds : IndunEvent
{
    public int MinDifficult { get; set; }
    public int MaxDifficult { get; set; }

    public override void Subscribe(WorldInstance worldInstance)
    {
        worldInstance.Events.OnIndunDifficultChanged += OnIndunDifficultChanged;
    }

    public override void UnSubscribe(WorldInstance worldInstance)
    {
        worldInstance.Events.OnIndunDifficultChanged -= OnIndunDifficultChanged;
    }

    private void OnIndunDifficultChanged(object sender, OnIndunDifficultChangedArgs args)
    {
        if (args == null || sender is not WorldInstance world)
            return;
        if (!IndunEventRules.DifficultInRange(args.Difficult, MinDifficult, MaxDifficult))
            return;

        Logger.Debug($"IndunEventDifficultChanged {Id}: difficulty {args.Difficult} in world {world.Id}");
        IndunManager.Instance.DoIndunActions(StartActionId, world);
    }
}
