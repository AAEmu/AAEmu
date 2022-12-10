using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.Quests;
using AAEmu.Game.Utils.DB;
using Microsoft.Data.Sqlite;
using NLog;
using QuestNpcAiName = AAEmu.Game.Models.Game.Quests.Static.QuestNpcAiName;

namespace AAEmu.Game.Core.Managers
{
    public class QuestManager : Singleton<QuestManager>, IQuestManager
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private bool _loaded = false;
        protected Dictionary<uint, QuestTemplate> _templates;
        protected Dictionary<byte, QuestSupplies> _supplies;
        protected Dictionary<uint, List<QuestAct>> _acts;
        private Dictionary<uint, QuestAct> _actsDic;
        protected Dictionary<string, Dictionary<uint, QuestActTemplate>> _actTemplates;
        private Dictionary<uint, List<uint>> _groupItems;
        private Dictionary<uint, List<uint>> _groupNpcs;
        public Dictionary<uint, Dictionary<uint, QuestTimeoutTask>> QuestTimeoutTask;

        public QuestTemplate GetTemplate(uint id)
        {
            return _templates.ContainsKey(id) ? _templates[id] : null;
        }

        public QuestSupplies GetSupplies(byte level)
        {
            return _supplies.ContainsKey(level) ? _supplies[level] : null;
        }

        public IQuestAct[] GetActs(uint id)
        {
            var res = (_acts.ContainsKey(id) ? _acts[id] : new List<QuestAct>()).ToArray();
            Array.Sort(res);
            return res;
        }

        public QuestActTemplate GetActTemplate(uint id, string type)
        {
            if (!_actTemplates.ContainsKey(type))
                return null;
            return _actTemplates[type].ContainsKey(id) ? _actTemplates[type][id] : null;
        }

        public T GetActTemplate<T>(uint id, string type) where T : QuestActTemplate
        {
            if (!_actTemplates.ContainsKey(type))
                return default(T);
            return _actTemplates[type].ContainsKey(id) ? (T)_actTemplates[type][id] : default(T);
        }

        public List<uint> GetGroupItems(uint groupId)
        {
            return _groupItems.ContainsKey(groupId) ? (_groupItems[groupId]) : new List<uint>();
        }

        public bool CheckGroupItem(uint groupId, uint itemId)
        {
            return _groupItems.ContainsKey(groupId) && (_groupItems[groupId].Contains(itemId));
        }

        public bool CheckGroupNpc(uint groupId, uint npcId)
        {
            return _groupNpcs.ContainsKey(groupId) && (_groupNpcs[groupId].Contains(npcId));
        }

        public void QuestCompleteTask(ICharacter owner, uint questId)
        {
            owner.Quests.Complete(questId, 0);
        }

        public void CancelQuest(ICharacter owner, uint questId)
        {
            owner.Quests.Drop(questId, true);
            owner.SendMessage("[Quest] {0}, quest {1} time is over, you didn't make it. Try again.", owner.Name, questId);
            _log.Warn("[Quest] {0}, quest {1} time is over, you didn't make it. Try again.", owner.Name, questId);
        }

        private void UpdateQuestComponentActs()
        {
            foreach(var questTemplate in _templates.Values)
            {
                foreach(var questComponent in questTemplate.Components)
                {
                    if (_acts.TryGetValue(questComponent.Key, out var questActs))
                        questComponent.Value.ActTemplates.AddRange(questActs.Select(a => a.Template));
                }
            }
        }
        public void Load()
        {
            if (_loaded)
                return;
            //                              charId          questId  Task
            QuestTimeoutTask = new Dictionary<uint, Dictionary<uint, QuestTimeoutTask>>();

            _templates = new Dictionary<uint, QuestTemplate>();
            _supplies = new Dictionary<byte, QuestSupplies>();
            _acts = new Dictionary<uint, List<QuestAct>>();
            _actsDic = new Dictionary<uint, QuestAct>();
            _actTemplates = new Dictionary<string, Dictionary<uint, QuestActTemplate>>();
            _groupItems = new Dictionary<uint, List<uint>>();
            _groupNpcs = new Dictionary<uint, List<uint>>();

            foreach (var type in Helpers.GetTypesInNamespace(Assembly.GetAssembly(typeof(QuestManager)), "AAEmu.Game.Models.Game.Quests.Acts"))
                if (type.BaseType == typeof(QuestActTemplate))
                    _actTemplates.Add(type.Name, new Dictionary<uint, QuestActTemplate>());

            _log.Info("Loading quests...");
            using (var connection = SQLite.CreateConnection())
            {
                LoadQuestContexts(connection);
                LoadQuestComponents(connection);
                LoadQuestSupplies(connection);
                LoadQuestActs(connection);

                LoadQuestActTemplates(connection);
                LoadQuestItemGroups(connection);
                LoadQuestMonsterNpcs(connection);

                UpdateQuestComponentActs();
            }
            _loaded = true;
        }

        private void LoadQuestMonsterNpcs(SqliteConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_monster_npcs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var groupId = reader.GetUInt32("quest_monster_group_id");
                        var npcId = reader.GetUInt32("npc_id");
                        List<uint> npcs;
                        if (!_groupNpcs.ContainsKey(groupId))
                        {
                            npcs = new List<uint>();
                            _groupNpcs.Add(groupId, npcs);
                        }
                        else
                            npcs = _groupNpcs[groupId];
                        npcs.Add(npcId);
                    }
                }
            }
        }

        private void LoadQuestItemGroups(SqliteConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_item_group_items";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var groupId = reader.GetUInt32("quest_item_group_id");
                        var itemId = reader.GetUInt32("item_id");
                        List<uint> items;
                        if (!_groupItems.ContainsKey(groupId))
                        {
                            items = new List<uint>();
                            _groupItems.Add(groupId, items);
                        }
                        else
                            items = _groupItems[groupId];

                        items.Add(itemId);
                    }
                }
            }
        }

        private void LoadQuestActs(SqliteConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_acts ORDER BY quest_component_id ASC, id ASC";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestAct();
                        template.Id = reader.GetUInt32("id");
                        template.ComponentId = reader.GetUInt32("quest_component_id");
                        template.DetailId = reader.GetUInt32("act_detail_id");
                        template.DetailType = reader.GetString("act_detail_type");
                        List<QuestAct> list;
                        if (_acts.ContainsKey(template.ComponentId))
                            list = _acts[template.ComponentId];
                        else
                        {
                            list = new List<QuestAct>();
                            _acts.Add(template.ComponentId, list);
                        }

                        list.Add(template);
                        _actsDic.Add(template.Id, template);
                    }
                }
            }
        }

        private void LoadQuestSupplies(SqliteConnection connection)
        {
            _log.Info("Loaded {0} quests", _templates.Count);
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_supplies";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestSupplies();
                        template.Id = reader.GetUInt32("id");
                        template.Level = reader.GetByte("level");
                        template.Exp = reader.GetInt32("exp");
                        template.Copper = reader.GetInt32("copper");
                        _supplies.Add(template.Level, template);
                    }
                }
            }
        }

        private void LoadQuestComponents(SqliteConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_components ORDER BY quest_context_id ASC, component_kind_id ASC, id ASC";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var questId = reader.GetUInt32("quest_context_id");
                        if (!_templates.ContainsKey(questId))
                            continue;

                        var template = new QuestComponent();
                        template.Id = reader.GetUInt32("id");
                        template.KindId = (QuestComponentKind)reader.GetByte("component_kind_id");
                        template.NextComponent = reader.GetUInt32("next_component", 0);
                        template.NpcAiId = (QuestNpcAiName)reader.GetUInt32("npc_ai_id", 0);
                        template.NpcId = reader.GetUInt32("npc_id", 0);
                        template.SkillId = reader.GetUInt32("skill_id", 0);
                        template.SkillSelf = reader.GetBoolean("skill_self", true);
                        template.AiPathName = reader.GetString("ai_path_name", string.Empty);
                        template.AiPathTypeId = (PathType)reader.GetUInt32("ai_path_type_id");
                        template.NpcSpawnerId = reader.GetUInt32("npc_spawner_id", 0);
                        template.PlayCinemaBeforeBubble = reader.GetBoolean("play_cinema_before_bubble", true);
                        template.AiCommandSetId = reader.GetUInt32("ai_command_set_id", 0);
                        template.OrUnitReqs = reader.GetBoolean("or_unit_reqs", true);
                        template.CinemaId = reader.GetUInt32("cinema_id", 0);
                        template.BuffId = reader.GetUInt32("buff_id", 0);
                        _templates[questId].Components.Add(template.Id, template);
                    }
                }
            }
        }

        private void LoadQuestContexts(SqliteConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_contexts ORDER BY id ASC";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestTemplate();
                        template.Id = reader.GetUInt32("id");
                        template.Repeatable = reader.GetBoolean("repeatable", true);
                        template.Level = reader.GetByte("level", 0);
                        template.Selective = reader.GetBoolean("selective", true);
                        template.Successive = reader.GetBoolean("successive", true);
                        template.RestartOnFail = reader.GetBoolean("restart_on_fail", true);
                        template.ChapterIdx = reader.GetUInt32("chapter_idx", 0);
                        template.QuestIdx = reader.GetUInt32("quest_idx", 0);
                        template.MilestoneId = reader.GetUInt32("milestone_id", 0);
                        template.LetItDone = reader.GetBoolean("let_it_done", true);
                        template.DetailId = reader.GetUInt32("detail_id");
                        template.ZoneId = reader.GetUInt32("zone_id");
                        template.Degree = reader.GetInt32("degree", 0);
                        template.UseQuestCamera = reader.GetBoolean("use_quest_camera", true);
                        template.Score = reader.GetInt32("score", 0);
                        template.UseAcceptMessage = reader.GetBoolean("use_accept_message", true);
                        template.UseCompleteMessage = reader.GetBoolean("use_complete_message", true);
                        template.GradeId = reader.GetUInt32("grade_id", 0);
                        _templates.Add(template.Id, template);
                    }
                }
            }
        }

        private void AddActTemplate(QuestActTemplate template)
        {
            var detailType = template.GetType().Name;
            _actTemplates[detailType].Add(template.Id, template);
            foreach (var questAct in _actsDic.Values.Where(qa => 
                    qa.DetailId == template.Id && 
                    qa.DetailType == detailType))
            {
                questAct.Template = template;
            }
        }
        private void LoadQuestActTemplates(SqliteConnection connection)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_check_complete_components";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActCheckCompleteComponent();
                        template.Id = reader.GetUInt32("id");
                        template.CompleteComponent = reader.GetUInt32("complete_component");
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_check_distances";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActCheckDistance();
                        template.Id = reader.GetUInt32("id");
                        template.WithIn = reader.GetBoolean("within", true);
                        template.NpcId = reader.GetUInt32("npc_id");
                        template.Distance = reader.GetInt32("distance");
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_check_guards";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActCheckGuard();
                        template.Id = reader.GetUInt32("id");
                        template.NpcId = reader.GetUInt32("npc_id");
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_check_spheres";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActCheckSphere();
                        template.Id = reader.GetUInt32("id");
                        template.SphereId = reader.GetUInt32("sphere_id");
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_check_timers";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActCheckTimer();
                        template.Id = reader.GetUInt32("id");
                        template.LimitTime = reader.GetInt32("limit_time");
                        template.ForceChangeComponent = reader.GetBoolean("force_change_component", true);
                        template.NextComponent = reader.GetUInt32("next_component");
                        template.PlaySkill = reader.GetBoolean("play_skill", true);
                        template.SkillId = reader.GetUInt32("skill_id", 0);
                        template.CheckBuff = reader.GetBoolean("check_buf", true);
                        template.BuffId = reader.GetUInt32("buff_id", 0);
                        template.SustainBuff = reader.GetBoolean("sustain_buf", true);
                        template.TimerNpcId = reader.GetUInt32("timer_npc_id", 0);
                        template.IsSkillPlayer = reader.GetBoolean("is_skill_player", true);
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_con_accept_buffs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActConAcceptBuff();
                        template.Id = reader.GetUInt32("id");
                        template.BuffId = reader.GetUInt32("buff_id");
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_con_accept_components";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActConAcceptComponent();
                        template.Id = reader.GetUInt32("id");
                        template.QuestContextId = reader.GetUInt32("quest_context_id");
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_con_accept_doodads";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActConAcceptDoodad();
                        template.Id = reader.GetUInt32("id");
                        template.DoodadId = reader.GetUInt32("doodad_id");
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_con_accept_item_equips";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActConAcceptItemEquip();
                        template.Id = reader.GetUInt32("id");
                        template.ItemId = reader.GetUInt32("item_id");
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_con_accept_item_gains";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActConAcceptItemGain();
                        template.Id = reader.GetUInt32("id");
                        template.ItemId = reader.GetUInt32("item_id");
                        template.Count = reader.GetInt32("count");
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_con_accept_items";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActConAcceptItem();
                        template.Id = reader.GetUInt32("id");
                        template.ItemId = reader.GetUInt32("item_id");
                        template.Cleanup = reader.GetBoolean("cleanup", true);
                        template.DropWhenDestroy = reader.GetBoolean("drop_when_destroy", true);
                        template.DestroyWhenDrop = reader.GetBoolean("destroy_when_drop", true);
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_con_accept_level_ups";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActConAcceptLevelUp();
                        template.Id = reader.GetUInt32("id");
                        template.Level = reader.GetByte("level");
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_con_accept_npc_emotions";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActConAcceptNpcEmotion();
                        template.Id = reader.GetUInt32("id");
                        template.NpcId = reader.GetUInt32("npc_id");
                        template.Emotion = reader.GetString("emotion");
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_con_accept_npc_kills";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActConAcceptNpcKill();
                        template.Id = reader.GetUInt32("id");
                        template.NpcId = reader.GetUInt32("npc_id");
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_con_accept_npcs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActConAcceptNpc();
                        template.Id = reader.GetUInt32("id");
                        template.NpcId = reader.GetUInt32("npc_id");
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_con_accept_skills";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActConAcceptSkill();
                        template.Id = reader.GetUInt32("id");
                        template.SkillId = reader.GetUInt32("skill_id");
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_con_accept_spheres";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActConAcceptSphere();
                        template.Id = reader.GetUInt32("id");
                        template.SphereId = reader.GetUInt32("sphere_id");
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_con_auto_completes";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActConAutoComplete();
                        template.Id = reader.GetUInt32("id");
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_con_fails";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActConFail();
                        template.Id = reader.GetUInt32("id");
                        template.ForceChangeComponent = reader.GetBoolean("force_change_component", true);
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_con_report_doodads";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActConReportDoodad();
                        template.Id = reader.GetUInt32("id");
                        template.DoodadId = reader.GetUInt32("doodad_id");
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id");
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_con_report_journals";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActConReportJournal();
                        template.Id = reader.GetUInt32("id");
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_con_report_npcs";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActConReportNpc();
                        template.Id = reader.GetUInt32("id");
                        template.NpcId = reader.GetUInt32("npc_id");
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_etc_item_obtains";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActEtcItemObtain();
                        template.Id = reader.GetUInt32("id");
                        template.ItemId = reader.GetUInt32("item_id");
                        template.Count = reader.GetInt32("count");
                        template.HighlightDooadId = reader.GetUInt32("highlight_doodad_id", 0);
                        template.Cleanup = reader.GetBoolean("cleanup", true);
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_ability_levels";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjAbilityLevel();
                        template.Id = reader.GetUInt32("id");
                        template.AbilityId = reader.GetByte("ability_id");
                        template.Level = reader.GetByte("level");
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_aggros";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjAggro();
                        template.Id = reader.GetUInt32("id");
                        template.Range = reader.GetInt32("range");
                        template.Rank1 = reader.GetInt32("rank1", 0);
                        template.Rank1Ratio = reader.GetInt32("rank1_ratio", 0);
                        template.Rank1Item = reader.GetBoolean("rank1_item", true);
                        template.Rank2 = reader.GetInt32("rank2", 0);
                        template.Rank2Ratio = reader.GetInt32("rank2_ratio", 0);
                        template.Rank2Item = reader.GetBoolean("rank2_item", true);
                        template.Rank3 = reader.GetInt32("rank3", 0);
                        template.Rank3Ratio = reader.GetInt32("rank3_ratio", 0);
                        template.Rank3Item = reader.GetBoolean("rank3_item", true);
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id");
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_complete_quests";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjCompleteQuest();
                        template.Id = reader.GetUInt32("id");
                        template.QuestId = reader.GetUInt32("quest_id");
                        template.AcceptWith = reader.GetBoolean("accept_with", true);
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_conditions";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjCondition();
                        template.Id = reader.GetUInt32("id");
                        template.ConditionId = reader.GetUInt32("condition_id");
                        template.QuestContextId = reader.GetUInt32("quest_context_id");
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_crafts";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjCraft();
                        template.Id = reader.GetUInt32("id");
                        template.CraftId = reader.GetUInt32("craft_id");
                        template.Count = reader.GetInt32("count");
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id");
                        template.HighlightDoodadId = reader.GetUInt32("highlight_doodad_id", 0);
                        template.HighlightDoodadPhase = reader.GetInt32("highlight_doodad_phase", -1); // TODO phase = 0?
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_distances";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjDistance();
                        template.Id = reader.GetUInt32("id");
                        template.WithIn = reader.GetBoolean("within", true);
                        template.NpcId = reader.GetUInt32("npc_id");
                        template.Distance = reader.GetInt32("distance");
                        template.HighlightDoodadId = reader.GetUInt32("highlight_doodad_id", 0);
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_doodad_phase_checks";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjDoodadPhaseCheck();
                        template.Id = reader.GetUInt32("id");
                        template.DoodadId = reader.GetUInt32("doodad_id");
                        template.Phase1 = reader.GetUInt32("phase1", 0);
                        template.Phase2 = reader.GetUInt32("phase2", 0);
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_effect_fires";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjEffectFire();
                        template.Id = reader.GetUInt32("id");
                        template.EffectId = reader.GetUInt32("effect_id");
                        template.Count = reader.GetInt32("count");
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_express_fires";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjExpressFire();
                        template.Id = reader.GetUInt32("id");
                        template.ExpressKeyId = reader.GetUInt32("express_key_id");
                        template.NpcGroupId = reader.GetUInt32("npc_group_id");
                        template.Count = reader.GetInt32("count");
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_interactions";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjInteraction();
                        template.Id = reader.GetUInt32("id");
                        template.WorldInteractionId = (WorldInteractionType)reader.GetInt32("wi_id");
                        template.Count = reader.GetInt32("count");
                        template.DoodadId = reader.GetUInt32("doodad_id", 0);
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.TeamShare = reader.GetBoolean("team_share", true);
                        template.HighlightDoodadId = reader.GetUInt32("highlight_doodad_id", 0);
                        template.HighlightDoodadPhase = reader.GetInt32("highlight_doodad_phase", -1); // TODO phase = 0?
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        template.Phase = reader.GetUInt32("phase", 0);
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_item_gathers";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjItemGather();
                        template.Id = reader.GetUInt32("id");
                        template.ItemId = reader.GetUInt32("item_id");
                        template.Count = reader.GetInt32("count");
                        template.HighlightDoodadId = reader.GetUInt32("highlight_doodad_id", 0);
                        template.HighlightDoodadPhase = reader.GetInt32("highlight_doodad_phase", -1); // TODO phase = 0?
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        template.Cleanup = reader.GetBoolean("cleanup", true);
                        template.DropWhenDestroy = reader.GetBoolean("drop_when_destroy", true);
                        template.DestroyWhenDrop = reader.GetBoolean("destroy_when_drop", true);
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_item_group_gathers";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjItemGroupGather();
                        template.Id = reader.GetUInt32("id");
                        template.ItemGroupId = reader.GetUInt32("item_group_id");
                        template.Count = reader.GetInt32("count");
                        template.Cleanup = reader.GetBoolean("cleanup", true);
                        template.HighlightDoodadId = reader.GetUInt32("highlight_doodad_id", 0);
                        template.HighlightDoodadPhase = reader.GetInt32("highlight_doodad_phase", -1); // TODO phase = 0?
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        template.DropWhenDestroy = reader.GetBoolean("drop_when_destroy", true);
                        template.DestroyWhenDrop = reader.GetBoolean("destroy_when_drop", true);
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_item_group_uses";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjItemGroupUse();
                        template.Id = reader.GetUInt32("id");
                        template.ItemGroupId = reader.GetUInt32("item_group_id");
                        template.Count = reader.GetInt32("count");
                        template.HighlightDoodadId = reader.GetUInt32("highlight_doodad_id", 0);
                        template.HighlightDoodadPhase = reader.GetInt32("highlight_doodad_phase", -1); // TODO phase = 0?
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        template.DropWhenDestroy = reader.GetBoolean("drop_when_destroy", true);
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_item_uses";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjItemUse();
                        template.Id = reader.GetUInt32("id");
                        template.ItemId = reader.GetUInt32("item_id");
                        template.Count = reader.GetInt32("count");
                        template.HighlightDoodadId = reader.GetUInt32("highlight_doodad_id", 0);
                        template.HighlightDoodadPhase = reader.GetInt32("highlight_doodad_phase", -1); // TODO phase = 0?
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        template.DropWhenDestroy = reader.GetBoolean("drop_when_destroy", true);
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_levels";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjLevel();
                        template.Id = reader.GetUInt32("id");
                        template.Level = reader.GetByte("level");
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_mate_levels";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjMateLevel();
                        template.Id = reader.GetUInt32("id");
                        template.ItemId = reader.GetUInt32("item_id");
                        template.Level = reader.GetByte("level");
                        template.Cleanup = reader.GetBoolean("cleanup", true);
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_monster_group_hunts";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjMonsterGroupHunt();
                        template.Id = reader.GetUInt32("id");
                        template.QuestMonsterGroupId = reader.GetUInt32("quest_monster_group_id");
                        template.Count = reader.GetInt32("count");
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        template.HighlightDoodadId = reader.GetUInt32("highlight_doodad_id", 0);
                        template.HighlightDoodadPhase = reader.GetInt32("highlight_doodad_phase", -1); // TODO phase = 0?
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_monster_hunts";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjMonsterHunt();
                        template.Id = reader.GetUInt32("id");
                        template.NpcId = reader.GetUInt32("npc_id");
                        template.Count = reader.GetInt32("count");
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        template.HighlightDoodadId = reader.GetUInt32("highlight_doodad_id", 0);
                        template.HighlightDoodadPhase = reader.GetInt32("highlight_doodad_phase", -1); // TODO phase = 0?
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_send_mails";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjSendMail();
                        template.Id = reader.GetUInt32("id");
                        template.ItemId1 = reader.GetUInt32("item1_id", 0);
                        template.Count1 = reader.GetInt32("count1");
                        template.ItemId2 = reader.GetUInt32("item2_id", 0);
                        template.Count2 = reader.GetInt32("count2");
                        template.ItemId3 = reader.GetUInt32("item3_id", 0);
                        template.Count3 = reader.GetInt32("count3");
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_spheres";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjSphere();
                        template.Id = reader.GetUInt32("id");
                        template.SphereId = reader.GetUInt32("sphere_id");
                        template.NpcId = reader.GetUInt32("npc_id", 0);
                        template.HighlightDoodadId = reader.GetUInt32("highlight_doodad_id", 0);
                        template.HighlightDoodadPhase = reader.GetInt32("highlight_doodad_phase", -1); // TODO phase = 0?
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_talk_npc_groups";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjTalkNpcGroup();
                        template.Id = reader.GetUInt32("id");
                        template.NpcGroupId = reader.GetUInt32("npc_group_id");
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_talks";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjTalk();
                        template.Id = reader.GetUInt32("id");
                        template.NpcId = reader.GetUInt32("npc_id");
                        template.TeamShare = reader.GetBoolean("team_share", true);
                        template.ItemId = reader.GetUInt32("item_id", 0);
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_zone_kills";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjZoneKill();
                        template.Id = reader.GetUInt32("id");
                        template.CountPlayerKill = reader.GetInt32("count_pk");
                        template.CountNpc = reader.GetInt32("count_npc");
                        template.ZoneId = reader.GetUInt32("zone_id", 0);
                        template.TeamShare = reader.GetBoolean("team_share", true);
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        template.LvlMin = reader.GetInt32("lv_min");
                        template.LvlMax = reader.GetInt32("lv_max");
                        template.IsParty = reader.GetBoolean("is_party", true);
                        template.LvlMinNpc = reader.GetInt32("lv_min_npc");
                        template.LvlMaxNpc = reader.GetInt32("lv_max_npc");
                        template.PcFactionId = reader.GetUInt32("pc_faction_id", 0);
                        template.PcFactionExclusive = reader.GetBoolean("pc_faction_exclusive", true);
                        template.NpcFactionId = reader.GetUInt32("npc_faction_id", 0);
                        template.NpcFactionExclusive = reader.GetBoolean("npc_faction_exclusive", true);
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_zone_monster_hunts";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjZoneMonsterHunt();
                        template.Id = reader.GetUInt32("id");
                        template.ZoneId = reader.GetUInt32("zone_id");
                        template.Count = reader.GetInt32("count");
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_zone_npc_talks";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjZoneNpcTalk();
                        template.Id = reader.GetUInt32("id");
                        template.NpcId = reader.GetUInt32("npc_id");
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_obj_zone_quest_completes";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActObjZoneQuestComplete();
                        template.Id = reader.GetUInt32("id");
                        template.ZoneId = reader.GetUInt32("zone_id");
                        template.Count = reader.GetInt32("count");
                        template.UseAlias = reader.GetBoolean("use_alias", true);
                        template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_supply_aa_points";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActSupplyAaPoint();
                        template.Id = reader.GetUInt32("id");
                        template.Point = reader.GetInt32("point");
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_supply_appellations";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActSupplyAppellation();
                        template.Id = reader.GetUInt32("id");
                        template.AppellationId = reader.GetUInt32("appellation_id");
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_supply_coppers";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActSupplyCopper();
                        template.Id = reader.GetUInt32("id");
                        template.Amount = reader.GetInt32("amount");
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_supply_crime_points";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActSupplyCrimePoint();
                        template.Id = reader.GetUInt32("id");
                        template.Point = reader.GetInt32("point");
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_supply_exps";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActSupplyExp();
                        template.Id = reader.GetUInt32("id");
                        template.Exp = reader.GetInt32("exp");
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_supply_honor_points";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActSupplyHonorPoint();
                        template.Id = reader.GetUInt32("id");
                        template.Point = reader.GetInt32("point");
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_supply_interactions";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActSupplyInteraction();
                        template.Id = reader.GetUInt32("id");
                        template.WorldInteractionId = (WorldInteractionType)reader.GetUInt32("wi_id");
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_supply_items";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActSupplyItem();
                        template.Id = reader.GetUInt32("id");
                        template.ItemId = reader.GetUInt32("item_id");
                        template.Count = reader.GetInt32("count");
                        template.GradeId = reader.GetByte("grade_id");
                        template.ShowActionBar = reader.GetBoolean("show_action_bar", true);
                        template.Cleanup = reader.GetBoolean("cleanup", true);
                        template.DropWhenDestroy = reader.GetBoolean("drop_when_destroy", true);
                        template.DestroyWhenDrop = reader.GetBoolean("destroy_when_drop", true);
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_supply_jury_points";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActSupplyJuryPoint();
                        template.Id = reader.GetUInt32("id");
                        template.Point = reader.GetInt32("point");
                        AddActTemplate(template);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_supply_living_points";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActSupplyLivingPoint();
                        template.Id = reader.GetUInt32("id");
                        template.Point = reader.GetInt32("point");
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_supply_lps";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActSupplyLp();
                        template.Id = reader.GetUInt32("id");
                        template.LaborPower = reader.GetInt32("lp");
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_supply_remove_items";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActSupplyRemoveItem();
                        template.Id = reader.GetUInt32("id");
                        template.ItemId = reader.GetUInt32("item_id");
                        template.Count = reader.GetInt32("count");
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_supply_selective_items";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActSupplySelectiveItem();
                        template.Id = reader.GetUInt32("id");
                        template.ItemId = reader.GetUInt32("item_id");
                        template.Count = reader.GetInt32("count");
                        template.GradeId = reader.GetByte("grade_id");
                        AddActTemplate(template);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM quest_act_supply_skills";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new QuestActSupplySkill();
                        template.Id = reader.GetUInt32("id");
                        template.SkillId = reader.GetUInt32("skill_id");
                        AddActTemplate(template);
                    }
                }
            }
        }
    }
}
