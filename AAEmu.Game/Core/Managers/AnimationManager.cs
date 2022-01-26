using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AAEmu.Commons.IO;
using AAEmu.Commons.Utils;
using AAEmu.Game.IO;
using AAEmu.Game.Models.Game.Animation;
using AAEmu.Game.Utils.DB;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class AnimationManager : Singleton<AnimationManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        private Dictionary<uint, Anim> _animations = new Dictionary<uint, Anim>();
        private Dictionary<string, Anim> _animationsByName = new Dictionary<string, Anim>();

        public Anim GetAnimation(uint id)
        {
            return _animations.ContainsKey(id) ? _animations[id] : null;
        }

        public Anim GetAnimation(string name)
        {
            return _animationsByName.ContainsKey(name) ? _animationsByName[name] : null;
        }

        /// <summary>
        /// Parse target .g file into a List<AnimCombatSyncEvent>
        /// </summary>
        /// <param name="gFileName"></param>
        /// <returns>Returns null if there is a error, otherwise returns the list</returns>
        private List<AnimCombatSyncEvent> ParseGFile(string gFileName)
        {
            var res = new List<AnimCombatSyncEvent>();
            var lines = ClientFileManager.GetFileAsString(gFileName).Split("\r\n");

            AnimCombatSyncEvent lastCombatSyncEvent = null;
            AnimDuration lastAnimDuration = null;
            for (var n = 0; n < lines.Length; n++)
            {
                var line = lines[n];
                var spaceCount = line.TakeWhile(char.IsWhiteSpace).Count();
                var trimmedLine = line.Trim(' ');
                if (spaceCount == 0)
                {
                    // Start of new model section
                    lastCombatSyncEvent = new AnimCombatSyncEvent();
                    lastCombatSyncEvent.ModelName = line.Trim('"');
                    res.Add(lastCombatSyncEvent);
                    lastAnimDuration = null;
                }
                else if ((lastCombatSyncEvent != null) && (spaceCount == 4))
                {
                    // Start of new animation section
                    lastAnimDuration = new AnimDuration();
                    if (!lastCombatSyncEvent.Animations.TryAdd(trimmedLine, lastAnimDuration))
                    {
                        _log.Warn($"Syntax error in {gFileName} at line {n + 1} : {line}");
                        return null;
                    }
                }
                else if ((lastAnimDuration != null) && (spaceCount == 8))
                {
                    // This is a actual property
                    var props = trimmedLine.Split(' ');
                    if (props.Length != 2)
                    {
                        _log.Warn($"Syntax error in {gFileName} at line {n + 1} : {line}");
                        return null;
                    }
                    else if (props[0] == "total_time")
                    {
                        if (int.TryParse(props[1], out var totTime))
                            lastAnimDuration.total_time = totTime;
                        else
                        {
                            _log.Warn($"int parse error in {gFileName} at line {n + 1} : {line}");
                            return null;
                        }
                    }
                    else if (props[0] == "combat_sync_time")
                    {
                        if (int.TryParse(props[1], out var syncTime))
                            lastAnimDuration.combat_sync_time = syncTime;
                        else
                        {
                            _log.Warn($"int parse error in {gFileName} at line {n + 1} : {line}");
                            return null;
                        }
                    }
                    else
                    {
                        _log.Warn($"Unknown property in {gFileName} at line {n + 1} : {line}");
                    }
                }
                else
                {
                    _log.Warn($"Unknown Syntax in {gFileName} at line {n + 1} : {line}");
                    return null;
                }
            }
            return res;
        }

        public void Load()
        {
            _animations = new Dictionary<uint, Anim>();
            _animationsByName = new Dictionary<string, Anim>();

            _log.Info("Loading animations...");

            using (var connection = SQLite.CreateConnection())
            {
                /* Anims */
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT * FROM anims";
                    command.Prepare();
                    using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                    {
                        while (reader.Read())
                        {
                            var template = new Anim()
                            {
                                Id = reader.GetUInt32("id"),
                                Name = reader.GetString("name"),
                                Loop = reader.GetBoolean("loop"),
                                Category = (AnimCategory)reader.GetUInt32("category_id"),
                                RideUB = reader.GetString("ride_ub"),
                                HangUB = reader.GetString("hang_ub"),
                                SwimUB = reader.GetString("swim_ub"),
                                MoveUB = reader.GetString("move_ub"),
                                RelaxedUB = reader.GetString("relaxed_ub"),
                                SwimMoveUB = reader.GetString("swim_move_ub")
                            };

                            _animations.Add(template.Id, template);
                            _animationsByName.Add(template.Name, template);
                        }
                    }
                }
            }

            // Load animation durations from client data
            var gFileName = "game/combat_sync_event_list.g"; 
            var combatSyncEvents = ParseGFile(gFileName);

            if (combatSyncEvents == null)
            {
                _log.Fatal($"Error reading {gFileName}");
                return;
            }
            
            // Apply values to our animation manager (only takes nuian_male into account as a base value)
            foreach (var cse in combatSyncEvents)
            {
                if (cse.ModelName == "nuian_male")
                {
                    // Copy stuff
                    foreach (var (animKey,animVal) in cse.Animations)
                    {
                        if (_animationsByName.TryGetValue(animKey, out var anim))
                        {
                            anim.Duration = animVal.total_time;
                            anim.CombatSyncTime = animVal.combat_sync_time;
                        }
                    }
                }
            }
        }
    }
}
