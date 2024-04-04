using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.AI.Enums;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.Quests;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

using QuestNpcAiName = AAEmu.Game.Models.Game.Quests.Static.QuestNpcAiName;

namespace AAEmu.Game.Core.Managers;

public class QuestManager : Singleton<QuestManager>, IQuestManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private bool _loaded;
    private readonly Dictionary<uint, QuestTemplate> _templates = new();
    private readonly Dictionary<byte, QuestSupplies> _supplies = new();

    /// <summary>
    /// ComponentId, Template
    /// </summary>
    private readonly Dictionary<uint, List<QuestActTemplate>> _actsByComponent = new();

    /// <summary>
    /// DetailType, DetailId, Template
    /// </summary>
    private readonly Dictionary<string, Dictionary<uint, QuestActTemplate>> _actTemplates = new();
    private readonly Dictionary<uint, List<uint>> _groupItems = new();
    private readonly Dictionary<uint, List<uint>> _groupNpcs = new();
    private readonly Dictionary<uint, QuestComponentTemplate> _templateComponents = new();
    public Dictionary<uint, Dictionary<uint, QuestTimeoutTask>> QuestTimeoutTask { get; } = new();

    /// <summary>
    /// Gets the Template of a Quest by TemplateId
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public QuestTemplate GetTemplate(uint id)
    {
        return _templates.GetValueOrDefault(id);
    }

    /// <summary>
    /// Gets the calculated Quest Supplies for a given character Level
    /// </summary>
    /// <param name="level"></param>
    /// <returns></returns>
    public QuestSupplies GetSupplies(byte level)
    {
        return _supplies.GetValueOrDefault(level);
    }

    /// <summary>
    /// Get Acts in Component
    /// </summary>
    /// <param name="id">ComponentId</param>
    /// <returns>Array of Acts</returns>
    public List<QuestActTemplate> GetActs(uint id)
    {
        var res = (_actsByComponent.TryGetValue(id, out var act) ? act : []);
        //Array.Sort(res); // На некоторых данных вызывает System.InvalidOperationException: Failed to compare two elements in the array. System.InvalidOperationException: Failed to compare two elements in the array.
        // 
        return res;
    }

    /// <summary>
    /// Gets QuestActTemplate by Type and Id
    /// </summary>
    /// <param name="id"></param>
    /// <param name="type"></param>
    /// <returns></returns>
    public QuestActTemplate GetActTemplate(uint id, string type)
    {
        return _actTemplates.GetValueOrDefault(type)?.GetValueOrDefault(id);
    }

    /// <summary>
    /// Gets QuestActTemplate&lt;T&gt; by Type and Id
    /// </summary>
    /// <param name="id"></param>
    /// <param name="type"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public T GetActTemplate<T>(uint id, string type) where T : QuestActTemplate
    {
        return (T)_actTemplates.GetValueOrDefault(type)?.GetValueOrDefault(id);
    }

    /// <summary>
    /// Gets list of ItemIds of a specific Quest Item Group
    /// </summary>
    /// <param name="groupId"></param>
    /// <returns></returns>
    public List<uint> GetGroupItems(uint groupId)
    {
        return _groupItems.TryGetValue(groupId, out var item) ? (item) : [];
    }

    /// <summary>
    /// Checks if a ItemId is part of a Quest Item GroupId
    /// </summary>
    /// <param name="groupId"></param>
    /// <param name="itemId"></param>
    /// <returns></returns>
    public bool CheckGroupItem(uint groupId, uint itemId)
    {
        return _groupItems.GetValueOrDefault(groupId)?.Contains(itemId) ?? false;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="groupId"></param>
    /// <param name="npcId"></param>
    /// <returns></returns>
    public bool CheckGroupNpc(uint groupId, uint npcId)
    {
        return _groupNpcs.ContainsKey(groupId) && (_groupNpcs[groupId].Contains(npcId));
    }

    /// <summary>
    /// Completes a quest (used by acts)
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="questId"></param>
    public void QuestCompleteTask(ICharacter owner, uint questId)
    {
        owner.Quests.CompleteQuest(questId, 0);
    }

    /// <summary>
    /// Fails the quest
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="questId"></param>
    public void FailQuest(ICharacter owner, uint questId)
    {
        if (!owner.Quests.ActiveQuests.TryGetValue(questId, out var quest))
        {
            Logger.Warn($"FailQuest triggered for a quest that isn't active, Quest:{questId}, Player:{owner.Name} ({owner.Id})");
            return;
        }

        quest.Step = QuestComponentKind.Fail;
        //owner.Quests.Drop(questId, true);
        owner.SendMessage($"[Quest] {owner.Name}, quest {questId} time is over, you didn't make it. Try again.");
        Logger.Debug($"[Quest] {owner.Name}, quest {questId} time is over, you didn't make it. Try again.");
    }

    /// <summary>
    /// Attach QuestActTemplates to QuestComponentTemplates, used during Quest Template loading
    /// </summary>
    private void UpdateQuestComponentActs()
    {
        foreach (var questTemplate in _templates.Values)
        {
            byte actIndex = 0;
            var lastKey = QuestComponentKind.None;
            foreach (var (questComponentKey, questComponentValue) in questTemplate.Components)
            {
                if (questComponentValue.KindId != lastKey)
                {
                    actIndex = 0;
                    lastKey = questComponentValue.KindId;
                }

                if (!_actsByComponent.TryGetValue(questComponentKey, out var questActs))
                    continue;

                // Assign references to parents
                foreach (var questAct in questActs)
                {
                    questAct.ThisComponentObjectiveIndex = actIndex;
                    questAct.ParentComponent = questComponentValue;
                    questAct.ParentQuestTemplate = questTemplate;

                    actIndex++;
                }

                // Actually add them
                foreach (var questAct in questActs)
                    questComponentValue.ActTemplates.Add(questAct);
            }
        }
    }

    /// <summary>
    /// Load Quest Templates
    /// </summary>
    public void Load()
    {
        if (_loaded)
            return;

        foreach (var type in Helpers.GetTypesInNamespace(Assembly.GetAssembly(typeof(QuestManager)), "AAEmu.Game.Models.Game.Quests.Acts"))
            if (type.BaseType == typeof(QuestActTemplate))
                _actTemplates.Add(type.Name, new Dictionary<uint, QuestActTemplate>());

        Logger.Info("Loading quests...");
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

        // Start daily reset task
        var dailyCron = "0 0 0 ? * * *";
        // TODO: Make sure it obeys server time settings
        TaskManager.Instance.CronSchedule(new QuestDailyResetTask(), dailyCron);
    }

    /// <summary>
    /// Function needed for a hack to make older quest starter items work
    /// </summary>
    /// <param name="itemTemplateId"></param>
    /// <returns></returns>
    public uint GetQuestIdFromStarterItem(uint itemTemplateId)
    {
        // This is a very ugly reverse search function
        foreach (var actTemplate in _actTemplates["QuestActConAcceptItem"].Values)
        {
            if (actTemplate is not QuestActConAcceptItem actAcceptItem)
                continue;
            if (actAcceptItem.ItemId != itemTemplateId)
                continue;

            // find quest_acts data
            foreach (var actList in _actsByComponent.Values)
            {
                foreach (var questAct in actList)
                {
                    if (questAct.DetailType == "QuestActConAcceptItem" && questAct.DetailId == actAcceptItem.DetailId)
                    {
                        // Use component Id to check if it's a starter, and return contextId (QuestId)
                        foreach (var (questId, questContext) in _templates)
                        {
                            if ((questContext.Components.TryGetValue(questAct.ParentComponent.Id, out var questComponent)) &&
                                (questComponent.KindId == QuestComponentKind.Start))
                                return questId;
                        }
                    }
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Simplified version of GetQuestIdFromStarterItem
    /// </summary>
    /// <param name="itemTemplateId">Item id</param>
    /// <returns>Gets the target quest which accepts the item</returns>
    public uint GetQuestIdFromStarterItemNew(uint itemTemplateId)
    {
        foreach (var foundActs in _actTemplates["QuestActConAcceptItem"].Values.Where(qAcceptItem => qAcceptItem is QuestActConAcceptItem qai && qai.ItemId == itemTemplateId))
        {
            var matchingAct = _actTemplates["QuestActConAcceptItem"].Values
                .FirstOrDefault(act =>
                    act.ParentComponent?.KindId == QuestComponentKind.Start && act.DetailId == foundActs.DetailId);

            if (matchingAct != null)
                return matchingAct.ParentComponent?.ParentQuestTemplate?.Id ?? 0;
        }
        return 0;
    }

    private void LoadQuestMonsterNpcs(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM quest_monster_npcs";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
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

    private void LoadQuestItemGroups(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM quest_item_group_items";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
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

    private void LoadQuestActs(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM quest_acts ORDER BY quest_component_id, id";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var componentId = reader.GetUInt32("quest_component_id");
            _templateComponents.TryGetValue(componentId, out var questComponentTemplate);
            var template = new QuestActTemplate(questComponentTemplate);

            template.ActId = reader.GetUInt32("id");
            template.DetailId = reader.GetUInt32("act_detail_id");
            template.DetailType = reader.GetString("act_detail_type");

            if (!_actsByComponent.TryGetValue(template.ParentComponent.Id, out var actInComponentList))
            {
                actInComponentList = new List<QuestActTemplate>();
                _actsByComponent.Add(template.ParentComponent.Id, actInComponentList);
            }
            actInComponentList.Add(template);
        }
    }

    private void LoadQuestSupplies(SqliteConnection connection)
    {
        Logger.Info($"Loaded {_templates.Count} quests");
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM quest_supplies";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
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

    private void LoadQuestComponents(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM quest_components ORDER BY quest_context_id, component_kind_id, id";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var questId = reader.GetUInt32("quest_context_id");
            if (!_templates.ContainsKey(questId))
                continue;

            var template = new QuestComponentTemplate(_templates[questId]);
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
            _templateComponents.TryAdd(template.Id, template);
        }
    }

    private void LoadQuestContexts(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM quest_contexts ORDER BY id";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
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
            template.DetailId = (QuestDetail)reader.GetUInt32("detail_id");
            template.ZoneId = reader.GetUInt32("zone_id");
            template.CategoryId = reader.GetUInt32("category_id");
            template.Degree = reader.GetInt32("degree", 0);
            template.UseQuestCamera = reader.GetBoolean("use_quest_camera", true);
            template.Score = reader.GetInt32("score", 0);
            template.UseAcceptMessage = reader.GetBoolean("use_accept_message", true);
            template.UseCompleteMessage = reader.GetBoolean("use_complete_message", true);
            template.GradeId = reader.GetUInt32("grade_id", 0);
            _templates.Add(template.Id, template);
        }
    }

    private void AddActTemplate(QuestActTemplate template)
    {
        var detailType = template.GetType().Name;
        _actTemplates[detailType].Add(template.DetailId, template);
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActCheckCompleteComponent", actId);
                    var template = new QuestActCheckCompleteComponent(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActCheckDistance", actId);
                    var template = new QuestActCheckDistance(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActCheckGuard", actId);
                    var template = new QuestActCheckGuard(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActCheckSphere", actId);
                    var template = new QuestActCheckSphere(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActCheckTimer", actId);
                    var template = new QuestActCheckTimer(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActConAcceptBuff", actId);
                    var template = new QuestActConAcceptBuff(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActConAcceptComponent", actId);
                    var template = new QuestActConAcceptComponent(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActConAcceptDoodad", actId);
                    var template = new QuestActConAcceptDoodad(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActConAcceptItemEquip", actId);
                    var template = new QuestActConAcceptItemEquip(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActConAcceptItemGain", actId);
                    var template = new QuestActConAcceptItemGain(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActConAcceptItem", actId);
                    var template = new QuestActConAcceptItem(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActConAcceptLevelUp", actId);
                    var template = new QuestActConAcceptLevelUp(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActConAcceptNpcEmotion", actId);
                    var template = new QuestActConAcceptNpcEmotion(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActConAcceptNpcKill", actId);
                    var template = new QuestActConAcceptNpcKill(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActConAcceptNpc", actId);
                    var template = new QuestActConAcceptNpc(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActConAcceptSkill", actId);
                    var template = new QuestActConAcceptSkill(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActConAcceptSphere", actId);
                    var template = new QuestActConAcceptSphere(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActConAutoComplete", actId);
                    var template = new QuestActConAutoComplete(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActConFail", actId);
                    var template = new QuestActConFail(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActConReportDoodad", actId);
                    var template = new QuestActConReportDoodad(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActConReportJournal", actId);
                    var template = new QuestActConReportJournal(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActConReportNpc", actId);
                    var template = new QuestActConReportNpc(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActEtcItemObtain", actId);
                    var template = new QuestActEtcItemObtain(parentComponent);
                    template.DetailId = actId;
                    template.ItemId = reader.GetUInt32("item_id");
                    template.Count = reader.GetInt32("count");
                    template.HighlightDoodadId = reader.GetUInt32("highlight_doodad_id", 0);
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjAbilityLevel", actId);
                    var template = new QuestActObjAbilityLevel(parentComponent);
                    template.DetailId = actId;
                    template.AbilityId = (AbilityType)reader.GetByte("ability_id");
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjAggro", actId);
                    var template = new QuestActObjAggro(parentComponent);
                    template.DetailId = actId;
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
            command.CommandText = "SELECT * FROM quest_act_obj_cinemas";
            command.Prepare();
            using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
            {
                while (reader.Read())
                {
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjCinema", actId);
                    var template = new QuestActObjCinema(parentComponent);
                    template.DetailId = actId;
                    template.CinemaId = reader.GetUInt32("cinema_id");
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjCompleteQuest", actId);
                    var template = new QuestActObjCompleteQuest(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjCondition", actId);
                    var template = new QuestActObjCondition(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjCraft", actId);
                    var template = new QuestActObjCraft(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjDistance", actId);
                    var template = new QuestActObjDistance(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjDoodadPhaseCheck", actId);
                    var template = new QuestActObjDoodadPhaseCheck(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjDoodadPhaseCheck", actId);
                    var template = new QuestActObjEffectFire(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjExpressFire", actId);
                    var template = new QuestActObjExpressFire(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjInteraction", actId);
                    var template = new QuestActObjInteraction(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjItemGather", actId);
                    var template = new QuestActObjItemGather(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjItemGroupGather", actId);
                    var template = new QuestActObjItemGroupGather(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjItemGroupUse", actId);
                    var template = new QuestActObjItemGroupUse(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjItemUse", actId);
                    var template = new QuestActObjItemUse(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjLevel", actId);
                    var template = new QuestActObjLevel(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjLevel", actId);
                    var template = new QuestActObjMateLevel(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjMonsterGroupHunt", actId);
                    var template = new QuestActObjMonsterGroupHunt(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjMonsterHunt", actId);
                    var template = new QuestActObjMonsterHunt(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjSendMail", actId);
                    var template = new QuestActObjSendMail(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjSphere", actId);
                    var template = new QuestActObjSphere(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjTalkNpcGroup", actId);
                    var template = new QuestActObjTalkNpcGroup(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjTalk", actId);
                    var template = new QuestActObjTalk(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjZoneKill", actId);
                    var template = new QuestActObjZoneKill(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjZoneMonsterHunt", actId);
                    var template = new QuestActObjZoneMonsterHunt(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjZoneNpcTalk", actId);
                    var template = new QuestActObjZoneNpcTalk(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActObjZoneQuestComplete", actId);
                    var template = new QuestActObjZoneQuestComplete(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActSupplyAaPoint", actId);
                    var template = new QuestActSupplyAaPoint(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActSupplyAppellation", actId);
                    var template = new QuestActSupplyAppellation(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActSupplyCopper", actId);
                    var template = new QuestActSupplyCopper(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActSupplyCrimePoint", actId);
                    var template = new QuestActSupplyCrimePoint(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActSupplyExp", actId);
                    var template = new QuestActSupplyExp(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActSupplyHonorPoint", actId);
                    var template = new QuestActSupplyHonorPoint(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActSupplyInteraction", actId);
                    var template = new QuestActSupplyInteraction(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActSupplyItem", actId);
                    var template = new QuestActSupplyItem(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActSupplyJuryPoint", actId);
                    var template = new QuestActSupplyJuryPoint(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActSupplyLivingPoint", actId);
                    var template = new QuestActSupplyLivingPoint(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActSupplyLp", actId);
                    var template = new QuestActSupplyLp(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActSupplyRemoveItem", actId);
                    var template = new QuestActSupplyRemoveItem(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActSupplySelectiveItem", actId);
                    var template = new QuestActSupplySelectiveItem(parentComponent);
                    template.DetailId = actId;
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
                    var actId = reader.GetUInt32("id");
                    var parentComponent = GetComponentByActTemplate("QuestActSupplySkill", actId);
                    var template = new QuestActSupplySkill(parentComponent);
                    template.DetailId = actId;
                    template.SkillId = reader.GetUInt32("skill_id");
                    AddActTemplate(template);
                }
            }
        }
    }

    /// <summary>
    /// Gets the Component that contains a specific Act Type and Id
    /// </summary>
    /// <param name="actDetailType"></param>
    /// <param name="actTemplateId"></param>
    /// <returns></returns>
    private QuestComponentTemplate GetComponentByActTemplate(string actDetailType, uint actTemplateId)
    {
        foreach (var componentTemplate in _templateComponents.Values)
        {
            foreach (var actTemplate in componentTemplate.ActTemplates)
            {
                if ((actTemplate.DetailId == actTemplateId) && (actTemplate.DetailType == actDetailType))
                    return componentTemplate;
            }
        }
        Logger.Error($"GetComponentByActTemplate no Component found that holds {actDetailType} {actTemplateId}");
        return null;
    }

    /// <summary>
    /// Creates a new Timer for a given quest
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="quest"></param>
    /// <param name="limitTime">in milliseconds</param>
    /// <returns>False if this quest already has a timer running</returns>
    public bool AddQuestTimer(ICharacter owner, Quest quest, int limitTime)
    {
        // Grab or Create the TimeOutTask list for this player
        if (!QuestTimeoutTask.TryGetValue(owner.Id, out var playerTimerTasks))
        {
            playerTimerTasks = new Dictionary<uint, QuestTimeoutTask>();
            QuestTimeoutTask.Add(owner.Id, playerTimerTasks);
        }

        // Check if this quest is already running a timer
        if (playerTimerTasks.ContainsKey(quest.TemplateId))
        {
            // Already has a timer running for this quest
            return false;
        }

        // Fill in the new end time for this quest
        quest.Time = DateTime.UtcNow.AddMilliseconds(limitTime);

        // Create new Task and add them to the dictionary for this player
        var timeoutTask = new QuestTimeoutTask(owner, quest.TemplateId);
        playerTimerTasks.Add(quest.TemplateId, timeoutTask);

        // Actually schedule the task
        TaskManager.Instance.Schedule(timeoutTask, TimeSpan.FromMilliseconds(limitTime));
        owner.SendMessage($"[Quest] Quest ({quest.Id}) will end in {limitTime / 60000} minutes.");
        return true;
    }

    public int RemoveQuestTimer(uint ownerId, uint questId)
    {
        var res = 0;
        if (QuestTimeoutTask.TryGetValue(ownerId, out var timeoutTasks))
        {
            var removeQuestList = new List<uint>();
            foreach (var (timeoutQuestId, timeoutTask) in timeoutTasks)
            {
                if ((questId == 0) || (questId == timeoutQuestId))
                {
                    removeQuestList.Add(timeoutQuestId);
                    _ = timeoutTask.CancelAsync(); // Cancel task, don't care about the result
                }
            }

            foreach (var q in removeQuestList)
                timeoutTasks.Remove(q);
        }
        return res;
    }

    /// <summary>
    /// Event to trigger the quest turn in to a NPC or Doodad
    /// </summary>
    /// <param name="owner">Player</param>
    /// <param name="questContextId">QuestId</param>
    /// <param name="npcObjId"></param>
    /// <param name="doodadObjId"></param>
    /// <param name="selected">Selected reward (if any)</param>
    public void DoReportEvents(ICharacter owner, uint questContextId, uint npcObjId, uint doodadObjId, int selected)
    {
        if (npcObjId > 0)
        {
            // Turning in at a NPC?
            var npc = WorldManager.Instance.GetNpc(npcObjId);
            // Is it a valid NPC?
            if (npc == null)
                return;

            //Connection.ActiveChar.Quests.OnReportToNpc(_npcObjId, _questContextId, _selected);
            // Initiate the event of Npc report on task completion
            owner.Events?.OnReportNpc(this, new OnReportNpcArgs
            {
                QuestId = questContextId,
                NpcId = npc.TemplateId,
                Selected = selected,
                Transform = npc.Transform
            });
        }
        else if (doodadObjId > 0)
        {
            // Turning in at a Doodad?
            var doodad = WorldManager.Instance.GetDoodad(doodadObjId);
            // Does the Doodad exist?
            if (doodad == null)
                return;

            //Connection.ActiveChar.Quests.OnReportToDoodad(_doodadObjId, _questContextId, _selected);
            // Trigger the Report to Doodad event
            owner.Events?.OnReportDoodad(this, new OnReportDoodadArgs
            {
                QuestId = questContextId,
                DoodadId = doodad.TemplateId,
                Selected = selected,
                Transform = doodad.Transform
            });
        }
        else
        {
            // Doesn't have a NPC or Doodad to turn in at, just auto-complete it
            owner.Quests.CompleteQuest(questContextId, selected, true);
        }
    }

    /// <summary>
    /// Trigger the quest events for handling the consumption of items
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="templateId"></param>
    /// <param name="count"></param>
    public void DoConsumedEvents(ICharacter owner, uint templateId, int count)
    {
        // Trigger the item use event
        owner?.Events?.OnItemUse(this, new OnItemUseArgs
        {
            ItemId = templateId,
            Count = count
        });

        // Trigger the item group use event
        // Check what groups this item belongs to
        // TODO: Optimize this to be added after item and quest loading
        var itemGroupsForThisItem = _groupItems.Where(x => x.Value.Contains(templateId)).Select(x => x.Key);
        foreach (var itemGroup in itemGroupsForThisItem)
        {
            owner?.Events?.OnItemGroupUse(this, new OnItemGroupUseArgs { ItemGroupId = templateId, Count = count });
        }
    }

    /// <summary>
    /// Trigger the quest events for acquiring items
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="templateId"></param>
    /// <param name="count"></param>
    public void DoAcquiredEvents(ICharacter owner, uint templateId, int count)
    {
        // Trigger the item acquire event
        owner?.Events?.OnItemGather(this, new OnItemGatherArgs
        {
            ItemId = templateId,
            Count = count
        });

        // Trigger the item group acquire event
        // Check what groups this item belongs to
        // TODO: Optimize this to be added after item and quest loading
        var itemGroupsForThisItem = _groupItems.Where(x => x.Value.Contains(templateId)).Select(x => x.Key);
        foreach (var itemGroup in itemGroupsForThisItem)
        {
            owner?.Events?.OnItemGroupGather(this, new OnItemGroupGatherArgs { ItemId = templateId, Count = count });
        }
    }

    /// <summary>
    /// Trigger the quest events to interacting with a Doodad
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="templateId"></param>
    public void DoDoodadInteractionEvents(ICharacter owner, uint templateId)
    {
        // Trigger the interaction event
        owner?.Events?.OnInteraction(this, new OnInteractionArgs
        {
            DoodadId = templateId
        });
    }

    /// <summary>
    /// Triggers the events for talking to a NPC
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="npcObjId"></param>
    /// <param name="questContextId"></param>
    /// <param name="questComponentId"></param>
    /// <param name="questActId"></param>
    public void DoTalkMadeEvents(ICharacter owner, uint npcObjId, uint questContextId, uint questComponentId, uint questActId)
    {
        var npc = WorldManager.Instance.GetNpc(npcObjId);
        if (npc == null)
            return;

        // Trigger talk to NPC event
        owner.Events?.OnTalkMade(this, new OnTalkMadeArgs
        {
            QuestId = questContextId,
            NpcId = npc.TemplateId,
            QuestComponentId = questComponentId,
            QuestActId = questActId,
            Transform = npc.Transform
        });

        // Trigger Talk to NPC group event
        var npcGroupsForThisNpc = _groupNpcs.Where(x => x.Value.Contains(npc.TemplateId)).Select(x => x.Key);
        foreach (var npcGroup in npcGroupsForThisNpc)
        {
            owner.Events?.OnTalkNpcGroupMade(this,
                new OnTalkNpcGroupMadeArgs
                {
                    QuestId = questContextId,
                    NpcGroupId = npc.TemplateId,
                    QuestComponentId = questComponentId,
                    QuestActId = questActId,
                    Transform = npc.Transform
                });
        }
    }

    /// <summary>
    /// Triggers the various events for killing a NPC
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="npc"></param>
    public void DoOnMonsterHuntEvents(ICharacter owner, Npc npc)
    {
        if (npc == null)
            return;

        var npcZoneGroupId = ZoneManager.Instance.GetZoneByKey(npc.Transform.ZoneId)?.GroupId ?? 0;

        // Individual monster kill
        owner.Events?.OnMonsterHunt(this, new OnMonsterHuntArgs
        {
            NpcId = npc.TemplateId,
            Count = 1,
            Transform = npc.Transform
        });

        // Trigger NPC Group kills
        var npcGroupsForThisNpc = _groupNpcs.Where(x => x.Value.Contains(npc.TemplateId)).Select(x => x.Key);
        foreach (var npcGroup in npcGroupsForThisNpc)
        {
            owner.Events?.OnMonsterGroupHunt(this, new OnMonsterGroupHuntArgs
            {
                NpcId = npcGroup,
                Count = 1,
                Position = npc.Transform
            });
        }

        // Trigger zone kills with specific Victim and Killer
        owner.Events?.OnZoneKill(this, new OnZoneKillArgs
        {
            ZoneGroupId = npcZoneGroupId,
            Killer = owner,
            Victim = npc
        });

        // Trigger any zone kills
        owner.Events?.OnZoneMonsterHunt(this, new OnZoneMonsterHuntArgs
        {
            ZoneGroupId = npcZoneGroupId
        });
    }

    /// <summary>
    /// Trigger Initial Aggro related quest events
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="npc"></param>
    public void DoOnAggroEvents(ICharacter owner, Npc npc)
    {
        if (npc == null)
            return;

        owner.Events?.OnAggro(this, new OnAggroArgs
        {
            NpcId = npc.TemplateId,
            Transform = npc.Transform
        });
    }

    /// <summary>
    /// Triggers emote related quest events
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="emotionId"></param>
    /// <param name="characterObjId">User</param>
    /// <param name="npcObjId">Target</param>
    public void DoOnExpressFireEvents(ICharacter owner, uint emotionId, uint characterObjId, uint npcObjId)
    {
        if (owner.ObjId != characterObjId)
        {
            Logger.Warn($"DoOnExpressFireEvents seems to have a invalid characterObjId referenced, Got:{characterObjId}, Expected:{owner.ObjId} ({owner.Name})");
            return;
        }
        var npc = WorldManager.Instance.GetNpc(npcObjId);
        if (npc == null)
            return;

        owner.Events?.OnExpressFire(this, new OnExpressFireArgs
        {
            NpcId = npc.TemplateId,
            EmotionId = emotionId
        });
    }

    /// <summary>
    /// Triggers a quest related check for levels
    /// </summary>
    /// <param name="owner"></param>
    public void DoOnLevelUpEvents(ICharacter owner)
    {
        owner.Events?.OnLevelUp(this, new OnLevelUpArgs());

        // Added for quest In the Footsteps of Gods and Heroes ( 5967 ), get all abilities (classes) to 50
        owner.Events?.OnAbilityLevelUp(this, new OnAbilityLevelUpArgs());

        // Also handle Level-based (character main level) quest starters
        // Un-started quests can't have a level event handler, so we need to do it this way for quest starters
        var levelActs = _actTemplates.GetValueOrDefault("QuestActConAcceptLevelUp")?.Values;
        if (levelActs != default)
            foreach (var levelAct in levelActs)
            {
                if ((levelAct is QuestActConAcceptLevelUp actLevelUp) && // correct Template
                    (owner.Level >= actLevelUp.Level) && // Minimum Level
                    !owner.Quests.HasQuestCompleted(actLevelUp.ParentQuestTemplate.Id) && // NEver completed before
                    !owner.Quests.HasQuest(actLevelUp.ParentQuestTemplate.Id)) // Not active
                {
                    // Start quest
                    owner.Quests.AddQuest(actLevelUp.ParentQuestTemplate.Id);
                }
            }
    }

    /// <summary>
    /// Triggers quest related events when a craft was successful
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="craftId"></param>
    public void DoOnCraftEvents(ICharacter owner, uint craftId)
    {
        // Added for quest Id=6024
        owner.Events?.OnCraft(this, new OnCraftArgs
        {
            CraftId = craftId
        });
    }

    /// <summary>
    /// Trigger quest events related to entering a QuestSphere
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="sphereQuest"></param>
    public void DoOnEnterSphereEvents(ICharacter owner, SphereQuest sphereQuest)
    {
        // Check if there's a active quest attached to this sphere
        var quest = owner.Quests.ActiveQuests.GetValueOrDefault(sphereQuest.QuestId);

        owner.Events?.OnEnterSphere(this, new OnEnterSphereArgs
        {
            SphereQuest = sphereQuest,
            OwningQuest = quest
        });
    }

    /// <summary>
    /// Trigger quest events related to exiting a QuestSphere
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="sphereQuest"></param>
    public void DoOnExitSphereEvents(ICharacter owner, SphereQuest sphereQuest)
    {
        // Check if there's a active quest attached to this sphere
        var quest = owner.Quests.ActiveQuests.GetValueOrDefault(sphereQuest.QuestId);

        owner.Events?.OnExitSphere(this, new OnExitSphereArgs
        {
            SphereQuest = sphereQuest,
            OwningQuest = quest
        });
    }
}
