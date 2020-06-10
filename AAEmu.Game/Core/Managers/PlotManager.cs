using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Plots.New;
using AAEmu.Game.Models.Game.Skills.Plots.Type;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class PlotManager : Singleton<PlotManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, Plot> _plots;
        private Dictionary<uint, PlotEventTemplate> _eventTemplates;
        private Dictionary<uint, PlotCondition> _conditions;

        private Dictionary<uint, NewPlot> _newPlots;
        private Dictionary<uint, NewPlotEventTemplate> _newPlotEvents;
        private Dictionary<uint, NewPlotCondition> _newPlotConditions;

        public Plot GetPlot(uint id)
        {
            if (_plots.ContainsKey(id))
                return _plots[id];
            return null;
        }

        public NewPlot GetNewPlot(uint id)
        {
            if (_newPlots.ContainsKey(id))
                return _newPlots[id];
            return null;
        }

        public PlotEventTemplate GetEventByPlotId(uint plotId)
        {
            if (_plots.ContainsKey(plotId))
                return _plots[plotId].EventTemplate;
            return null;
        }

        public void Load()
        {
            _plots = new Dictionary<uint, Plot>();
            _eventTemplates = new Dictionary<uint, PlotEventTemplate>();
            _conditions = new Dictionary<uint, PlotCondition>();
            
            _newPlots = new Dictionary<uint, NewPlot>();
            _newPlotEvents = new Dictionary<uint, NewPlotEventTemplate>();
            _newPlotConditions = new Dictionary<uint, NewPlotCondition>();
            
            using (var connection = SQLite.CreateConnection())
            {
                _log.Info("Loading plots...");
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM plots";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new Plot();
                            template.Id = reader.GetUInt32("id");
                            template.TargetTypeId = reader.GetUInt32("target_type_id");
                            _plots.Add(template.Id, template);

                            var newTemplate = new NewPlot
                            {
                                Id = reader.GetUInt32("id"),
                                TargetTypeId = (SkillTargetType)reader.GetUInt32("target_type_id")
                            };
                            
                            _newPlots.Add(newTemplate.Id, newTemplate);
                        }
                    }
                }

                _log.Info("Loaded {0} plots", _plots.Count);

                _log.Info("Loading plot events...");
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM plot_events";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new PlotEventTemplate();
                            template.Id = reader.GetUInt32("id");
                            template.PlotId = reader.GetUInt32("plot_id");
                            template.Position = reader.GetInt32("position");
                            template.SourceUpdateMethodId = reader.GetUInt32("source_update_method_id");
                            template.TargetUpdateMethodId = reader.GetUInt32("target_update_method_id");
                            template.TargetUpdateMethodParam1 = reader.GetInt32("target_update_method_param1");
                            template.TargetUpdateMethodParam2 = reader.GetInt32("target_update_method_param2");
                            template.TargetUpdateMethodParam3 = reader.GetInt32("target_update_method_param3");
                            template.TargetUpdateMethodParam4 = reader.GetInt32("target_update_method_param4");
                            template.TargetUpdateMethodParam5 = reader.GetInt32("target_update_method_param5");
                            template.TargetUpdateMethodParam6 = reader.GetInt32("target_update_method_param6");
                            template.TargetUpdateMethodParam7 = reader.GetInt32("target_update_method_param7");
                            template.TargetUpdateMethodParam8 = reader.GetInt32("target_update_method_param8");
                            template.TargetUpdateMethodParam9 = reader.GetInt32("target_update_method_param9");
                            template.Tickets = reader.GetInt32("tickets");
                            template.AoeDiminishing = reader.GetBoolean("aoe_diminishing", true);
                            _eventTemplates.Add(template.Id, template);

                            if (template.Position == 1 && _plots.ContainsKey(template.PlotId))
                                _plots[template.PlotId].EventTemplate = template;

                            var newTemplate = new NewPlotEventTemplate
                            {
                                Id = reader.GetUInt32("id"),
                                Plot = _newPlots[reader.GetUInt32("plot_id")],
                                Position = reader.GetInt32("position"),
                                SourceUpdateMethod =
                                    (PlotSourceUpdateMethodType)reader.GetUInt32("source_update_method_id"),
                                TargetUpdateMethodType =
                                    (PlotTargetUpdateMethodType)reader.GetUInt32("target_update_method_id"),
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
                                AoeDiminishing = reader.GetBoolean("aoe_diminishing", true),
                            };
                            
                            _newPlotEvents.Add(newTemplate.Id, newTemplate);

                            if (newTemplate.Position == 1)
                                newTemplate.Plot.FirstEvent = newTemplate;
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
                            var template = new PlotCondition();
                            template.Id = reader.GetUInt32("id");
                            template.NotCondition = reader.GetBoolean("not_condition", true);
                            template.Kind = (PlotConditionType) reader.GetInt32("kind_id");
                            template.Param1 = reader.GetInt32("param1");
                            template.Param2 = reader.GetInt32("param2");
                            template.Param3 = reader.GetInt32("param3");
                            _conditions.Add(template.Id, template);

                            var newTemplate = new NewPlotCondition
                            {
                                Id = reader.GetUInt32("id"),
                                NotCondition = reader.GetBoolean("not_condition", true),
                                Kind = (PlotConditionType)reader.GetInt32("kind_id"),
                                Param1 = reader.GetInt32("param1"),
                                Param2 = reader.GetInt32("param2"),
                                Param3 = reader.GetInt32("param3")
                            };
                            _newPlotConditions.Add(newTemplate.Id, newTemplate);
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
                            var template = new PlotEventCondition();
                            template.Condition = _conditions[condId];
                            template.Position = reader.GetInt32("position");
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
                    command.CommandText = "SELECT * FROM plot_event_conditions";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var id = reader.GetUInt32("event_id");
                            var condId = reader.GetUInt32("condition_id");
                            var template = new PlotEventCondition();
                            template.Condition = _conditions[condId];
                            template.Position = reader.GetInt32("position");
                            template.SourceId = reader.GetInt32("source_id");
                            template.TargetId = reader.GetInt32("target_id");
                            // TODO 1.2 // template.NotifyFailure = reader.GetBoolean("notify_failure", true);
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
                            
                            var newTemplate = new NewPlotEventCondition()
                            {
                                Condition = _newPlotConditions[reader.GetUInt32("condition_id")],
                                Event = _newPlotEvents[reader.GetUInt32("event_id")],
                                Position = reader.GetUInt32("position"),
                                Source = (PlotEffectSource) reader.GetUInt32("source_id"),
                                Target = (PlotEffectSource) reader.GetUInt32("target_id"),
                                NotifyFailure = reader.GetBoolean("notify_failure", true)
                            };
                            
                            newTemplate.Event.Conditions.Add(newTemplate.Position, newTemplate);
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
                            var template = new PlotEventEffect();
                            template.Position = reader.GetInt32("position");
                            template.SourceId = reader.GetInt32("source_id");
                            template.TargetId = reader.GetInt32("target_id");
                            template.ActualId = reader.GetUInt32("actual_id");
                            template.ActualType = reader.GetString("actual_type");
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
                            
                            var newTemplate = new NewPlotEffect()
                            {
                                Position = reader.GetUInt32("position"),
                                Source = (PlotEffectSource) reader.GetUInt32("source_id"),
                                Target = (PlotEffectSource) reader.GetUInt32("target_id"),
                                EffectId = reader.GetUInt32("actual_id"),
                                EffectType = reader.GetString("actual_type"),
                                Event = _newPlotEvents[reader.GetUInt32("event_id")]
                            };
                            
                            newTemplate.Event.Effects.Add(newTemplate.Position, newTemplate);
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
                            
                            var newTemplate = new NewPlotNextEventTemplate()
                            {
                                Event = _newPlotEvents[reader.GetUInt32("event_id")],
                                Position = reader.GetUInt32("position"),
                                NextEvent = _newPlotEvents[reader.GetUInt32("next_event_id")],
                                PerTarget = reader.GetBoolean("per_target", true),
                                Casting = reader.GetBoolean("casting", true),
                                Delay = reader.GetInt32("delay"),
                                Speed = reader.GetInt32("speed"),
                                Channeling = reader.GetBoolean("channeling", true),
                                CastingInc = reader.GetInt32("casting_inc"),
                                AddAnimCsTime = reader.GetBoolean("add_anim_cs_time", true),
                                CastingDelayable = reader.GetBoolean("casting_delayable", true),
                                CastingCancelable = reader.GetBoolean("casting_cancelable", true),
                                CancelOnBigHit = reader.GetBoolean("cancel_on_big_hit", true),
                                UseExeTime = reader.GetBoolean("use_exe_time", true),
                                Fail = reader.GetBoolean("fail", true),
                            };
                            
                            newTemplate.Event.NextEvents.Add(newTemplate.Position, newTemplate);
                        }
                    }
                }

                _log.Info("Loaded {0} plot events", _eventTemplates.Count);
            }
        }
    }
}
