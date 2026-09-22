using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Indun.Events;

/// <summary>
/// <c>indun_event_doodad_phase_changeds</c> (14 rows, zone groups 125, 126, 130): fires when the doodad
/// <c>doodad_almighty_id</c> settles on phase <c>doodad_func_group_id</c> inside a copy, filtered by
/// <c>check_status_id</c> against the copy's round timer as the phase change captured it (see
/// <see cref="IndunRoundRules.PhaseCheckMatches"/>).
/// Every round chain of the three round zone groups starts from one of these.
/// </summary>
internal class IndunEventDoodadPhaseChangeds : IndunEvent
{
    public uint DoodadAlmightyId { get; set; }
    public uint DoodadFuncGroupId { get; set; }
    public uint CheckStatusId { get; set; }

    public override void Subscribe(WorldInstance worldInstance)
    {
        worldInstance.Events.OnDoodadPhaseChanged += OnDoodadPhaseChanged;
    }

    public override void UnSubscribe(WorldInstance worldInstance)
    {
        worldInstance.Events.OnDoodadPhaseChanged -= OnDoodadPhaseChanged;
    }

    private void OnDoodadPhaseChanged(object sender, OnDoodadPhaseChangedArgs args)
    {
        if (args?.Doodad == null || sender is not WorldInstance world)
            return;
        if (args.Doodad.TemplateId != DoodadAlmightyId || args.FuncGroupId != DoodadFuncGroupId)
            return;

        // The value the phase change captured, not a live read: two rows watch doodad 13555 phase 39273
        // (208 "1RO clear(+4)" on status 2, 209 "1RO Clear(+1)" on status 3) and the first one's chain
        // ends the round, which stops the timer.
        var timerRunning = args.RoundTimerRunning;
        if (!IndunRoundRules.PhaseCheckMatches(CheckStatusId, timerRunning))
            return;

        Logger.Debug($"IndunEventDoodadPhaseChanged {Id}: doodad {DoodadAlmightyId} phase {DoodadFuncGroupId} in world {world.Id}, timer {(timerRunning ? "running" : "off")}");
        IndunManager.Instance.DoIndunActions(StartActionId, world);
    }
}
