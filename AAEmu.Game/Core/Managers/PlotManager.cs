using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Plots.Tree;
using AAEmu.Game.Models.Game.Skills.Plots.Type;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class PlotManager : Singleton<PlotManager>, IPlotManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private bool _loaded = false;

    private Dictionary<uint, Plot> _plots;
    private Dictionary<uint, PlotEventTemplate> _eventTemplates;
    private Dictionary<uint, PlotCondition> _conditions;
    //private Dictionary<uint, PlotAoeCondition> _aoeConditions;

    public Plot GetPlot(uint id)
    {
        if (_plots.TryGetValue(id, out var plot))
            return plot;
        return null;
    }

    public PlotEventTemplate GetEventByPlotId(uint plotId)
    {
        if (_plots.TryGetValue(plotId, out var plot))
            return plot.EventTemplate;
        return null;
    }

    public void Load()
    {
        if (_loaded)
            return;

        _plots = [];
        _eventTemplates = [];
        _conditions = [];
        //_aoeConditions = new Dictionary<uint, PlotAoeCondition>();
        using (var connection = SQLite.CreateConnection())
        {
            Logger.Info("Loading plots...");
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM plots";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new Plot { Id = reader.GetUInt32("id"), TargetTypeId = reader.GetUInt32("target_type_id") };
                        _plots.Add(template.Id, template);
                    }
                }
            }

            Logger.Info("Loaded {0} plots", _plots.Count);

            Logger.Info("Loading plot events...");
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM plot_events";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new PlotEventTemplate
                        {
                            Id = reader.GetUInt32("id"), PlotId = reader.GetUInt32("plot_id"), Position = reader.GetInt32("position"),
                            SourceUpdateMethodId = reader.GetUInt32("source_update_method_id"),
                            TargetUpdateMethodId = reader.GetUInt32("target_update_method_id"),
                            TargetUpdateMethodParam1 = reader.GetInt32("target_update_method_param1"),
                            TargetUpdateMethodParam2 = reader.GetInt32("target_update_method_param2"),
                            TargetUpdateMethodParam3 = reader.GetInt32("target_update_method_param3"),
                            TargetUpdateMethodParam4 = reader.GetInt32("target_update_method_param4"),
                            TargetUpdateMethodParam5 = reader.GetInt32("target_update_method_param5"),
                            TargetUpdateMethodParam6 = reader.GetInt32("target_update_method_param6"),
                            TargetUpdateMethodParam7 = reader.GetInt32("target_update_method_param7"),
                            TargetUpdateMethodParam8 = reader.GetInt32("target_update_method_param8"),
                            TargetUpdateMethodParam9 = reader.GetInt32("target_update_method_param9"),
                            Tickets = reader.GetInt32("tickets"),
                            AoeDiminishing = reader.GetBoolean("aoe_diminishing", true)
                        };
                        _eventTemplates.Add(template.Id, template);

                        if (template.Position == 1 && _plots.TryGetValue(template.PlotId, out var plot))
                            plot.EventTemplate = template;
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM plot_conditions";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new PlotCondition
                        {
                            Id = reader.GetUInt32("id"),
                            NotCondition = reader.GetBoolean("not_condition", true),
                            Kind = (PlotConditionType)reader.GetInt32("kind_id"),
                            Param1 = reader.GetInt32("param1"),
                            Param2 = reader.GetInt32("param2"),
                            Param3 = reader.GetInt32("param3"),
                            // Kind 20 (unit_reqs) carries its checks in unit_reqs rows owned by this condition
                            // rather than in param1..3 — 1519 of the 1605 rows leave all three at 0. This flag
                            // decides whether those rows are ANDed or ORed, exactly as skills.or_unit_reqs does.
                            OrUnitReqs = reader.GetBoolean("or_unit_reqs", true)
                        };
                        _conditions.Add(template.Id, template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM plot_event_conditions";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var id = reader.GetUInt32("event_id");
                        var condId = reader.GetUInt32("condition_id");
                        var template = new PlotEventCondition
                        {
                            Condition = _conditions[condId], Position = reader.GetInt32("position"), SourceId = (PlotEffectSource)reader.GetInt32("source_id"),
                            TargetId = (PlotEffectTarget)reader.GetInt32("target_id")
                        };
                        template.NotifyFailure = reader.GetBoolean("notify_failure", true);
                        var plotEvent = _eventTemplates[id];
                        if (plotEvent.Conditions.Count > 0)
                        {
                            var res = false;
                            for (var node = plotEvent.Conditions.First; node != null; node = node.Next)
                                if (node.Value.Position > template.Position)
                                {
                                    plotEvent.Conditions.AddBefore(node, template);
                                    res = true;
                                    break;
                                }

                            if (!res)
                                plotEvent.Conditions.AddLast(template);
                        }
                        else
                            plotEvent.Conditions.AddFirst(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM plot_aoe_conditions";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var id = reader.GetUInt32("event_id");
                        var condId = reader.GetUInt32("condition_id");
                        var template = new PlotAoeCondition { Condition = _conditions[condId], Position = reader.GetInt32("position") };
                        var plotEvent = _eventTemplates[id];
                        if (plotEvent.AoeConditions.Count > 0)
                        {
                            var res = false;
                            for (var node = plotEvent.AoeConditions.First; node != null; node = node.Next)
                                if (node.Value.Position > template.Position)
                                {
                                    plotEvent.AoeConditions.AddBefore(node, template);
                                    res = true;
                                    break;
                                }

                            if (!res)
                                plotEvent.AoeConditions.AddLast(template);
                        }
                        else
                            plotEvent.AoeConditions.AddFirst(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM plot_effects";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var id = reader.GetUInt32("event_id");
                        var template = new PlotEventEffect
                        {
                            Position = reader.GetInt32("position"),
                            SourceId = (PlotEffectSource)reader.GetInt32("source_id"),
                            TargetId = (PlotEffectTarget)reader.GetInt32("target_id"),
                            ActualId = reader.GetUInt32("actual_id"),
                            ActualType = reader.GetString("actual_type")
                        };
                        var evnt = _eventTemplates[id];
                        if (evnt.Effects.Count > 0)
                        {
                            var res = false;
                            for (var node = evnt.Effects.First; node != null; node = node.Next)
                                if (node.Value.Position > template.Position)
                                {
                                    evnt.Effects.AddBefore(node, template);
                                    res = true;
                                    break;
                                }

                            if (!res)
                                evnt.Effects.AddLast(template);
                        }
                        else
                            evnt.Effects.AddFirst(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM plot_next_events";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new PlotNextEvent();
                        var id = reader.GetUInt32("event_id");
                        var nextId = reader.GetUInt32("next_event_id");
                        template.Id = reader.GetUInt32("id");
                        template.Event = _eventTemplates[nextId];
                        template.Position = reader.GetInt32("position");
                        template.PerTarget = reader.GetBoolean("per_target", true);
                        template.Casting = reader.GetBoolean("casting", true);
                        template.Delay = reader.GetInt32("delay");
                        template.Speed = reader.GetInt32("speed");
                        template.Channeling = reader.GetBoolean("channeling", true);
                        template.CastingInc = reader.GetInt32("casting_inc");
                        template.AddAnimCsTime = reader.GetBoolean("add_anim_cs_time", true);
                        template.CastingDelayable = reader.GetBoolean("casting_delayable", true);
                        template.CastingCancelable = reader.GetBoolean("casting_cancelable", true);
                        template.CancelOnBigHit = reader.GetBoolean("cancel_on_big_hit", true);
                        template.UseExeTime = reader.GetBoolean("use_exe_time", true);
                        template.Fail = reader.GetBoolean("fail", true);
                        var plotEvent = _eventTemplates[id];
                        if (plotEvent.NextEvents.Count > 0)
                        {
                            var res = false;
                            for (var node = plotEvent.NextEvents.First; node != null; node = node.Next)
                                if (node.Value.Position > template.Position)
                                {
                                    plotEvent.NextEvents.AddBefore(node, template);
                                    res = true;
                                    break;
                                }

                            if (!res)
                                plotEvent.NextEvents.AddLast(template);
                        }
                        else
                            plotEvent.NextEvents.AddFirst(template);
                    }
                }
            }

            Logger.Info("Loaded {0} plot events", _eventTemplates.Count);

            foreach (var plot in _plots.Values)
            {
                if (plot.EventTemplate != null)
                    plot.Tree = PlotBuilder.BuildTree(plot.Id);
            }
            // Task.Run(() => flameboltTree.Execute(new PlotState()));
        }

        _loaded = true;
    }
}
