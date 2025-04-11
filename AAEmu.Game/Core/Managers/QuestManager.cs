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
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Tasks.Quests;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

using QuestNpcAiName = AAEmu.Game.Models.Game.Quests.Static.QuestNpcAiName;

namespace AAEmu.Game.Core.Managers;

public partial class QuestManager : Singleton<QuestManager>, IQuestManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private bool _loaded;
    private readonly Dictionary<uint, QuestTemplate> _questTemplates = [];
    private readonly Dictionary<byte, QuestSupplies> _supplies = [];

    /// <summary>
    /// ComponentId, Template
    /// </summary>
    private readonly Dictionary<uint, List<QuestActTemplate>> _actsByComponent = [];
    private readonly Dictionary<uint, QuestActTemplate> _actsBaseByActId = [];

    /// <summary>
    /// DetailType, DetailId, Template
    /// </summary>
    private readonly Dictionary<string, Dictionary<uint, QuestActTemplate>> _actTemplatesByDetailType = [];
    private readonly Dictionary<uint, List<uint>> _groupItems = [];
    private readonly Dictionary<uint, List<uint>> _groupNpcs = [];
    private readonly Dictionary<uint, QuestComponentTemplate> _componentTemplates = [];
    public Dictionary<uint, Dictionary<uint, QuestTimeoutTask>> QuestTimeoutTask { get; } = [];
    private Queue<Quest> EvaluationQueue { get; } = new();
    private readonly object _evaluationQueueLock = new();

    /// <summary>
    /// Gets the Template of a Quest by TemplateId
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public QuestTemplate GetTemplate(uint id)
    {
        return _questTemplates.GetValueOrDefault(id);
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
    public List<QuestActTemplate> GetActsInComponent(uint id)
    {
        return (_componentTemplates.TryGetValue(id, out var componentTemplate) ? componentTemplate.ActTemplates : []);
    }

    /// <summary>
    /// Gets QuestActTemplate by Type and Id
    /// </summary>
    /// <param name="id"></param>
    /// <param name="type"></param>
    /// <returns></returns>
    public QuestActTemplate GetActTemplate(uint id, string type)
    {
        return _actTemplatesByDetailType.GetValueOrDefault(type)?.GetValueOrDefault(id);
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
        return (T)_actTemplatesByDetailType.GetValueOrDefault(type)?.GetValueOrDefault(id);
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
    /// Adds a quest to a Evaluation request queue
    /// </summary>
    /// <param name="quest"></param>
    public void EnqueueEvaluation(Quest quest)
    {
        lock (_evaluationQueueLock)
        {
            var needNewTask = EvaluationQueue.Count <= 0;
            if (!EvaluationQueue.Contains(quest))
                EvaluationQueue.Enqueue(quest);

            Logger.Info($"EnqueueEvaluation, {quest.Owner.Name} ({quest.Owner.Id}), Quest {quest.TemplateId}");

            if (needNewTask)
                TaskManager.Instance.Schedule(new QuestManagerRunQueueTask(), null, TimeSpan.FromMilliseconds(1));
        }
    }

    /// <summary>
    /// Executes the Evaluation Request Queue
    /// </summary>
    public void DoQueuedEvaluations()
    {
        lock (_evaluationQueueLock)
        {
            while (EvaluationQueue.Count > 0)
            {
                var quest = EvaluationQueue.Dequeue();
                quest.StartingEvaluation();
                Logger.Info($"DoQueuedEvaluations, {quest.Owner.Name} ({quest.Owner.Id}), Quest {quest.TemplateId}");
                var currentResult = quest.RunCurrentStep();
            }
        }
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
        Logger.Debug($"[Quest] {owner.Name}, quest {questId} failed.");
    }

    /// <summary>
    /// Attach QuestActTemplates to QuestComponentTemplates, used during Quest Template loading
    /// </summary>
    private void UpdateQuestComponentActs()
    {
        foreach (var questTemplate in _questTemplates.Values)
        {
            byte actIndex = 0;
            byte selectiveRewardIndex = 0;
            var lastKey = QuestComponentKind.None;
            foreach (var (questComponentKey, questComponentValue) in questTemplate.Components)
            {
                if (questComponentValue.KindId != lastKey)
                {
                    actIndex = 0;
                    lastKey = questComponentValue.KindId;
                }

                var questActs = GetActsInComponent(questComponentKey);
                if (questActs.Count <= 0)
                    continue;

                // Assign references to parents
                foreach (var questAct in questActs)
                {
                    questAct.ThisComponentObjectiveIndex = questAct.CountsAsAnObjective ? actIndex : (byte)0xFF;
                    questAct.ParentQuestTemplate = questTemplate;

                    // For selective rewards
                    if (questAct is QuestActSupplySelectiveItem)
                    {
                        selectiveRewardIndex++;
                        questAct.ThisSelectiveIndex = selectiveRewardIndex;
                    }

                    if (questAct.CountsAsAnObjective)
                        actIndex++;
                }
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
                _actTemplatesByDetailType.Add(type.Name, []);

        Logger.Info("Loading quests...");
        using (var connection = SQLite.CreateConnection())
        {
            LoadQuestSupplies(connection);

            LoadQuestContexts(connection);
            LoadQuestComponents(connection);
            LoadBaseQuestActs(connection);

            LoadDetailQuestActTemplates(connection);
            LoadQuestItemGroups(connection);
            LoadQuestMonsterNpcs(connection);

            UpdateQuestComponentActs();
        }
        Logger.Info($"Loaded {_questTemplates.Count} quests");
        _loaded = true;

        // Start daily reset task
        var dailyCron = "0 0 0 */1 * *"; // Crontab
        // TODO: Make sure it obeys server time settings
        TaskManager.Instance.CronSchedule(new QuestDailyResetTask(), dailyCron);
    }

    /// <summary>
    /// Function needed for a hack to make older quest starter items work
    /// </summary>
    /// <param name="itemTemplateId">Item Template to check</param>
    /// <returns>Quest Id the item is supposed to start</returns>
    public uint GetQuestIdFromStarterItem(uint itemTemplateId)
    {
        // This is a very ugly reverse search function
        foreach (var actTemplate in _actTemplatesByDetailType["QuestActConAcceptItem"].Values)
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
                        foreach (var (questId, questContext) in _questTemplates)
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
        foreach (var foundActs in _actTemplatesByDetailType["QuestActConAcceptItem"].Values.Where(qAcceptItem => qAcceptItem is QuestActConAcceptItem questActConAcceptItem && questActConAcceptItem.ItemId == itemTemplateId))
        {
            var matchingAct = _actTemplatesByDetailType["QuestActConAcceptItem"].Values
                .FirstOrDefault(act =>
                    act.ParentComponent?.KindId == QuestComponentKind.Start && act.DetailId == foundActs.DetailId);

            if (matchingAct != null)
                return matchingAct.ParentComponent?.ParentQuestTemplate?.Id ?? 0;
        }
        return 0;
    }

    /// <summary>
    /// Load NPC group data
    /// </summary>
    /// <param name="connection"></param>
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
            if (!_groupNpcs.TryGetValue(groupId, out var npcIdList))
            {
                npcs = [];
                _groupNpcs.Add(groupId, npcs);
            }
            else
                npcs = npcIdList;
            npcs.Add(npcId);
        }
    }

    /// <summary>
    /// Load Item group data
    /// </summary>
    /// <param name="connection"></param>
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
            if (!_groupItems.TryGetValue(groupId, out var itemList))
            {
                items = [];
                _groupItems.Add(groupId, items);
            }
            else
                items = itemList;

            items.Add(itemId);
        }
    }

    /// <summary>
    /// Load Quest Acts
    /// </summary>
    /// <param name="connection"></param>
    private void LoadBaseQuestActs(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM quest_acts ORDER BY quest_component_id, id";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var componentId = reader.GetUInt32("quest_component_id");
            var actId = reader.GetUInt32("id");
            if (!_componentTemplates.TryGetValue(componentId, out var questComponentTemplate))
            {
                Logger.Trace($"LoadQuestActs: ActId {actId} references quest ComponentId {componentId} that does not exist");
                continue;
            }
            var template = new QuestActTemplate(questComponentTemplate);

            template.ActId = actId;
            template.DetailId = reader.GetUInt32("act_detail_id");
            template.DetailType = reader.GetString("act_detail_type");

            // Populate _actsByComponent
            if (!_actsByComponent.TryGetValue(template.ParentComponent.Id, out var actInComponentList))
            {
                actInComponentList = [];
                _actsByComponent.Add(template.ParentComponent.Id, actInComponentList);
            }
            actInComponentList.Add(template);

            // Add to parent component's ActTemplate List
            // questComponentTemplate.ActTemplates.Add(template);

            // Add to Base Act Cache 
            _actsBaseByActId.Add(template.ActId, template);
        }
    }

    /// <summary>
    /// Loads the default rewards table for xp/copper based on level
    /// </summary>
    /// <param name="connection"></param>
    private void LoadQuestSupplies(SqliteConnection connection)
    {
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

    /// <summary>
    /// Loads quest components
    /// </summary>
    /// <param name="connection"></param>
    private void LoadQuestComponents(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM quest_components ORDER BY quest_context_id, component_kind_id, id";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var questId = reader.GetUInt32("quest_context_id");
            if (!_questTemplates.TryGetValue(questId, out var questTemplate))
                continue;

            var template = new QuestComponentTemplate(questTemplate);
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
            _componentTemplates.Add(template.Id, template);

            questTemplate.Components.Add(template.Id, template);
        }
    }

    /// <summary>
    /// Load Quest main data
    /// </summary>
    /// <param name="connection"></param>
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
            _questTemplates.Add(template.Id, template);
        }
    }

    /// <summary>
    /// Adds QuestAct_xxx class into _actTemplates[]
    /// </summary>
    /// <param name="template"></param>
    private void AddActTemplate(QuestActTemplate template)
    {
        var detailType = template.GetType().Name;
        var baseAct = _actsBaseByActId.Values.FirstOrDefault(x => (x.DetailId == template.DetailId) && (x.DetailType == detailType));
        template.ActId = baseAct?.ActId ?? 0;
        template.ParentComponent.ActTemplates.Add(template);
        _actTemplatesByDetailType[detailType].Add(template.DetailId, template);
    }

    /// <summary>
    /// Loads all quest_act_xxx tables
    /// </summary>
    /// <param name="connection"></param>
    private void LoadDetailQuestActTemplates(SqliteConnection connection)
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
                    var template = new QuestActObjZoneKill(parentComponent);
                    template.DetailId = actId;
                    template.CountPlayerKill = reader.GetInt32("count_pk");
                    template.CountNpc = reader.GetInt32("count_npc");
                    template.Count = Math.Max(template.CountNpc, template.CountPlayerKill); // Exception since we have 2 possible values here
                    template.ZoneId = reader.GetUInt32("zone_id", 0);
                    template.TeamShare = reader.GetBoolean("team_share", true);
                    template.UseAlias = reader.GetBoolean("use_alias", true);
                    template.QuestActObjAliasId = reader.GetUInt32("quest_act_obj_alias_id", 0);
                    template.LvlMin = reader.GetInt32("lv_min");
                    template.LvlMax = reader.GetInt32("lv_max");
                    template.IsParty = reader.GetBoolean("is_party", true);
                    template.LvlMinNpc = reader.GetInt32("lv_min_npc");
                    template.LvlMaxNpc = reader.GetInt32("lv_max_npc");
                    template.PcFactionId = (FactionsEnum)reader.GetUInt32("pc_faction_id", 0);
                    template.PcFactionExclusive = reader.GetBoolean("pc_faction_exclusive", true);
                    template.NpcFactionId = (FactionsEnum)reader.GetUInt32("npc_faction_id", 0);
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
                    var template = new QuestActSupplyInteraction(parentComponent);
                    template.DetailId = actId;
                    template.WiId = (WorldInteractionType)reader.GetUInt32("wi_id");
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
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
                    if (parentComponent == null)
                        continue;
                    var template = new QuestActSupplySkill(parentComponent);
                    template.DetailId = actId;
                    template.SkillId = reader.GetUInt32("skill_id");
                    AddActTemplate(template);
                }
            }
        }
    }

    /// <summary>
    /// Gets the Component that contains a specific Act Type and Id (uses base cache)
    /// </summary>
    /// <param name="actDetailType">Act Detail Type</param>
    /// <param name="actTemplateId">Act Detail Id</param>
    /// <returns></returns>
    private QuestComponentTemplate GetComponentByActTemplate(string actDetailType, uint actTemplateId)
    {
        foreach (var (baseActId, baseActTemplate) in _actsBaseByActId)
        {
            if ((baseActTemplate.DetailType == actDetailType) && (baseActTemplate.DetailId == actTemplateId))
            {
                return baseActTemplate.ParentComponent;
            }
        }

        Logger.Trace($"GetComponentByActTemplate no Component found that holds {actDetailType} {actTemplateId}");
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
            playerTimerTasks = [];
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
        owner.SendDebugMessage($"[Quest] Quest ({quest.Id}) will end in {limitTime / 60000} minutes.");
        return true;
    }

    /// <summary>
    /// Removes quest related timer(s)
    /// </summary>
    /// <param name="ownerId"></param>
    /// <param name="questId">QuestId to be removed, or all timers if zero</param>
    /// <returns>Number of timers removed</returns>
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
                    timeoutTask.Cancel(); // Cancel task, don't care about the result
                }
            }

            foreach (var q in removeQuestList)
            {
                timeoutTasks.Remove(q);
                res++;
            }
        }
        return res;
    }

    public QuestComponentTemplate GetComponent(uint componentId)
    {
        return _componentTemplates.GetValueOrDefault(componentId);
    }
}
