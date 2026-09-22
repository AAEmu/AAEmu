using AAEmu.Commons.Utils;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Schedules;
using AAEmu.Game.Models.Game.Weather;

using NLog;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Schedule-driven world weather cycle. Each configured phase binds one <c>game_schedules</c> row
/// and names the <see cref="WeatherState"/> that row's window produces, so every timing comes from
/// content and none ships with the feature. Phases are evaluated against one UTC clock reading:
/// no open phase means <see cref="WeatherState.Clear"/>, exactly one open phase decides the state,
/// and an overlapping set is reported loudly while the previous state is kept rather than guessed.
/// </summary>
/// <remarks>
/// Restart policy: nothing is persisted. Every boot re-derives the state from the configured
/// phases, their content rows and the current clock — the same policy as the global snow state,
/// which is likewise re-seeded from its configured source at startup.
/// </remarks>
public class WeatherManager : Singleton<WeatherManager>
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    private sealed record Phase(WeatherState State, int ScheduleId);

    private readonly object _sync = new();
    private readonly List<Phase> _phases = [];
    private readonly List<string> _configurationErrors = [];
    private readonly HashSet<string> _reportedErrors = [];

    private Func<int, GameSchedules> _scheduleLookup;
    private bool _disabledReported;

    /// <summary>State decided by the most recent <see cref="Refresh"/>.</summary>
    public WeatherState CurrentState { get; private set; } = WeatherState.Clear;

    /// <summary>
    /// Raised once per state change with (previous, current). Handlers run outside the manager
    /// lock and a failing handler is logged instead of aborting the refresh.
    /// </summary>
    public event Action<WeatherState, WeatherState> StateChanged;

    /// <summary>
    /// Binds the phase configuration and the content lookup. Configuration problems (a state name
    /// that is not a <see cref="WeatherState"/>, a schedule id that cannot name a row) are kept as
    /// per-phase errors: the phase is skipped and every later refresh reports the problem again.
    /// </summary>
    public void Configure(WeatherConfig config, Func<int, GameSchedules> scheduleLookup)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(scheduleLookup);

        lock (_sync)
        {
            _scheduleLookup = scheduleLookup;
            _phases.Clear();
            _configurationErrors.Clear();
            _reportedErrors.Clear();
            _disabledReported = false;

            foreach (var phase in config.Phases ?? [])
            {
                if (phase is null)
                    continue;

                if (phase.ScheduleId <= 0)
                {
                    _configurationErrors.Add(
                        $"weather phase '{phase.State}': ScheduleId must name a game_schedules row");
                    continue;
                }

                if (!Enum.TryParse(phase.State, ignoreCase: true, out WeatherState state) ||
                    !Enum.IsDefined(typeof(WeatherState), state))
                {
                    _configurationErrors.Add(
                        $"weather phase for game schedule {phase.ScheduleId}: '{phase.State}' is not a weather state; phase skipped");
                    continue;
                }

                _phases.Add(new Phase(state, phase.ScheduleId));
            }
        }
    }

    /// <summary>
    /// Re-evaluates every configured phase at <paramref name="moment"/> and applies the result.
    /// Returns the full outcome, including every problem seen this pass; problems are logged once
    /// per distinct message so a periodic caller does not repeat itself.
    /// </summary>
    public WeatherRefreshResult Refresh(DateTime moment)
    {
        var now = ServerCalendar.AsUtc(moment);
        var errors = new List<string>();
        var open = new List<(Phase Phase, int ScheduleId)>();
        WeatherState previous;
        WeatherState next;
        int? activeScheduleId;

        lock (_sync)
        {
            previous = CurrentState;

            if (_scheduleLookup == null || (_phases.Count == 0 && _configurationErrors.Count == 0))
            {
                var reason = _scheduleLookup == null
                    ? "dynamic weather is disabled: no weather cycle is configured"
                    : "dynamic weather is disabled: no weather phases are configured";
                if (!_disabledReported)
                {
                    Logger.Warn(reason);
                    _disabledReported = true;
                }

                return new WeatherRefreshResult
                {
                    Disabled = true,
                    PreviousState = previous,
                    CurrentState = previous,
                    Errors = [reason],
                };
            }

            errors.AddRange(_configurationErrors);

            foreach (var phase in _phases)
            {
                GameSchedules schedule;
                try
                {
                    schedule = _scheduleLookup(phase.ScheduleId);
                }
                catch (Exception ex)
                {
                    errors.Add(
                        $"weather phase {phase.State}: game schedule {phase.ScheduleId} lookup failed ({ex.Message}); phase skipped");
                    continue;
                }

                if (schedule == null)
                {
                    errors.Add(
                        $"weather phase {phase.State}: game schedule {phase.ScheduleId} has no row; phase skipped");
                    continue;
                }

                bool openNow;
                try
                {
                    openNow = WeatherScheduleWindow.Evaluate(schedule, now);
                }
                catch (Exception ex)
                {
                    errors.Add(
                        $"weather phase {phase.State}: game schedule {phase.ScheduleId} row cannot be evaluated ({ex.Message}); phase skipped");
                    continue;
                }

                if (openNow)
                    open.Add((phase, phase.ScheduleId));
            }

            if (open.Count > 1)
            {
                errors.Add(
                    $"weather phases overlap: game schedules {string.Join(", ", open.Select(entry => entry.ScheduleId))} are open together; keeping {CurrentState}");
                next = CurrentState;
                activeScheduleId = null;
            }
            else if (open.Count == 1)
            {
                next = open[0].Phase.State;
                activeScheduleId = open[0].ScheduleId;
            }
            else
            {
                next = WeatherState.Clear;
                activeScheduleId = null;
            }

            CurrentState = next;

            foreach (var error in errors)
            {
                if (_reportedErrors.Add(error))
                    Logger.Error("Weather schedule problem: {0}", error);
            }
        }

        if (next != previous)
        {
            Logger.Info("Weather state {0} → {1}", previous, next);
            try
            {
                StateChanged?.Invoke(previous, next);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Weather state change handler failed ({0} → {1})", previous, next);
            }
        }

        return new WeatherRefreshResult
        {
            Disabled = false,
            PreviousState = previous,
            CurrentState = next,
            ActiveScheduleId = activeScheduleId,
            Errors = errors,
        };
    }
}

/// <summary>Outcome of one <see cref="WeatherManager.Refresh"/> pass.</summary>
public sealed class WeatherRefreshResult
{
    /// <summary>True when no weather cycle is configured; the state was left untouched.</summary>
    public bool Disabled { get; init; }

    public WeatherState PreviousState { get; init; }

    public WeatherState CurrentState { get; init; }

    public bool Transitioned => PreviousState != CurrentState;

    /// <summary>The content row that decided a non-clear state this pass, if any.</summary>
    public int? ActiveScheduleId { get; init; }

    /// <summary>Every problem seen this pass: bad configuration, missing rows, overlaps.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];
}
