using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Quests;

public partial class Quest : PacketMarshaler
{
    private const int MaxObjectiveCount = 5;
    private readonly ISphereQuestManager _sphereQuestManager;
    private readonly IQuestManager _questManager;
    private readonly ITaskManager _taskManager;
    private readonly ISkillManager _skillManager;
    private readonly IExpressTextManager _expressTextManager;
    private readonly IWorldManager _worldManager;
    private QuestComponentKind _step;

    /// <summary>
    /// DB ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// Quest Template Id
    /// </summary>
    public uint TemplateId { get; set; }

    /// <summary>
    /// Quest Template
    /// </summary>
    public IQuestTemplate Template { get; set; }

    /// <summary>
    /// Objective counters for the Progress step
    /// </summary>
    internal int[] Objectives { get; set; }

    /// <summary>
    /// Used to check Progress step
    /// </summary>
    public List<bool> ProgressStepResults { get; set; } = new();

    /// <summary>
    /// Current Quest Status
    /// </summary>
    public QuestStatus Status { get; set; }

    /// <summary>
    /// Current Quest Step
    /// </summary>
    public QuestComponentKind Step
    {
        get => _step;
        set => SetStep(value);
    }

    /// <summary>
    /// Objective Condition
    /// </summary>
    public QuestConditionObj Condition { get; set; }

    /// <summary>
    /// Is this quest ready to be turned in at a NPC
    /// </summary>
    public bool ReadyToReportNpc { get; set; }

    /// <summary>
    /// End time for timed quests
    /// </summary>
    public DateTime Time { get; set; }

    /// <summary>
    /// Owning character of this Quest Object
    /// </summary>
    public ICharacter Owner { get; set; }

    /// <summary>
    /// Remaining time for this quest in milliseconds
    /// </summary>
    private int LeftTime => Time > DateTime.UtcNow ? (int)(Time - DateTime.UtcNow).TotalMilliseconds : -1;
    private int SupplyItem { get; set; }
    public bool IsCheckSet { get; set; }

    /// <summary>
    /// DoodadId used in the Quest Packet
    /// </summary>
    public long DoodadId { get; set; }

    /// <summary>
    /// ObjId used in the Quest Packet (3 instances)
    /// </summary>
    private long ObjId { get; set; }

    // TODO: Check how this is actually supposed to behave in the packets
    /// <summary>
    /// ComponentId used in the SCQuestContext Packet
    /// </summary>
    public uint ComponentId { get; set; }

    /// <summary>
    /// Helper var for tracking the component we're working with
    /// </summary>
    public uint CurrentComponentId { get; set; }

    /// <summary>
    /// AcceptorType used for SCQuestContext
    /// </summary>
    public QuestAcceptorType QuestAcceptorType { get; set; }

    /// <summary>
    /// Acceptor Template Id of the QuestAcceptorType source where we got this quest from, used by SCQuestContext
    /// </summary>
    public uint AcceptorId { get; set; }

    /// <summary>
    /// Item pool of rewards
    /// </summary>
    public List<ItemCreationDefinition> QuestRewardItemsPool { get; set; }

    /// <summary>
    /// Item pool of items that are included in the cleanup process of this quest
    /// </summary>
    public List<ItemCreationDefinition> QuestCleanupItemsPool { get; set; }

    /// <summary>
    /// Money reward for this quest
    /// </summary>
    public int QuestRewardCoinsPool { get; set; }

    /// <summary>
    /// Exp reward for this quest
    /// </summary>
    public int QuestRewardExpPool { get; set; }

    /// <summary>
    /// Current QuestStep, or null if the current step is invalid
    /// </summary>
    public QuestStep CurrentStep => QuestSteps.GetValueOrDefault(Step);

    /// <summary>
    /// Set to false if item rewards have been disabled by objectives (mostly used by Aggro quests)
    /// </summary>
    public bool AllowItemRewards { get; set; } = true;

    /// <summary>
    /// Percent that the reward should be scaled to (mostly used by Aggro quests)
    /// </summary>
    public double QuestRewardRatio { get; set; } = 1.0;

    private bool _questInitializationFinished;
    private bool _requestEvaluationFlag;
    /// <summary>
    /// Set if this quests is requesting a re-evaluation of its steps/components/acts to check if it has been completed
    /// Set by the RequestEvaluation function, call after objective changes
    /// </summary>
    private bool RequestEvaluationFlag
    {
        get => _requestEvaluationFlag;
        set
        {
            if (_requestEvaluationFlag == value)
                return;
            _requestEvaluationFlag = value;
            if (value && _questInitializationFinished)
            {
                _questManager.EnqueueEvaluation(this);
            }
        }
    }

    private bool _skipUpdatePacket;

    public void SkipUpdatePackets()
    {
        _skipUpdatePacket = true;
    }

    /// <summary>
    /// The last chosen selective reward index for this quest
    /// </summary>
    public int SelectedRewardIndex { get; set; }

    /// <summary>
    /// Create Quest object
    /// </summary>
    /// <param name="questTemplate"></param>
    /// <param name="owner"></param>
    /// <param name="questManager"></param>
    /// <param name="sphereQuestManager"></param>
    /// <param name="taskManager"></param>
    /// <param name="skillManager"></param>
    /// <param name="expressTextManager"></param>
    /// <param name="worldManager"></param>
    public Quest(IQuestTemplate questTemplate, ICharacter owner, IQuestManager questManager, ISphereQuestManager sphereQuestManager,
        ITaskManager taskManager, ISkillManager skillManager, IExpressTextManager expressTextManager,
        IWorldManager worldManager)
    {
        Owner = owner;
        _questManager = questManager;
        _sphereQuestManager = sphereQuestManager;
        _taskManager = taskManager;
        _skillManager = skillManager;
        _expressTextManager = expressTextManager;
        _worldManager = worldManager;

        if (questTemplate is not null)
        {
            TemplateId = questTemplate.Id;
            Template = questTemplate;

            CreateQuestSteps();
        }

        _step = QuestComponentKind.Invalid;
        Status = QuestStatus.Invalid;

        Objectives = new int[MaxObjectiveCount];
        SupplyItem = 0;
        ObjId = 0;
        QuestRewardItemsPool = new List<ItemCreationDefinition>();
        QuestCleanupItemsPool = new List<ItemCreationDefinition>();
        ReadyToReportNpc = false;

        InitializeQuestActs();
    }

    public Quest(ICharacter owner) : this(null, owner, QuestManager.Instance, SphereQuestManager.Instance, TaskManager.Instance, SkillManager.Instance, ExpressTextManager.Instance, WorldManager.Instance)
    {
        // Nothing extra
    }

    public Quest(IQuestTemplate template, ICharacter owner) : this(template, owner, QuestManager.Instance, SphereQuestManager.Instance, TaskManager.Instance, SkillManager.Instance, ExpressTextManager.Instance, WorldManager.Instance)
    {
        // Nothing Extra
    }

    /// <summary>
    /// Use Skill на себя или на Npc, с которым взаимодействуем (Use Skill on yourself or on the Npc you interact with)
    /// </summary>
    /// <param name="component"></param>
    public void UseSkillAndBuff(QuestComponentTemplate component)
    {
        if (component == null) { return; }
        UseSkill(component);
        UseBuff(component);
    }

    private void UseBuff(QuestComponentTemplate component)
    {
        if (component.BuffId > 0)
        {
            Owner.Buffs.AddBuff(new Buff(Owner, Owner, SkillCaster.GetByType(SkillCasterType.Unit), _skillManager.GetBuffTemplate(component.BuffId), null, DateTime.UtcNow));
        }
    }

    /// <summary>
    /// Use Skill defined in QuestComponent on yourself or on the Npc you interact with
    /// </summary>
    /// <param name="component"></param>
    private void UseSkill(QuestComponentTemplate component)
    {
        if (component.SkillId > 0)
        {
            if (component.SkillSelf)
            {
                Owner.UseSkill(component.SkillId, Owner);
            }
            else if (component.NpcId > 0)
            {
                var npc = _worldManager.GetNpcByTemplateId(component.NpcId);
                npc?.UseSkill(component.SkillId, npc);
            }
        }
    }

    /// <summary>
    /// If NpcAiId is AttackUnit, change the Npc faction to Monstrosity
    /// </summary>
    /// <param name="component"></param>
    public void SetNpcAggro(QuestComponentTemplate component)
    {
        if (component == null) { return; }
        if (component.NpcAiId == QuestNpcAiName.AttackUnit)
        {
            if (component.NpcId > 0)
            {
                var npc = _worldManager.GetNpcByTemplateId(component.NpcId);
                npc?.SetFaction(FactionsEnum.Monstrosity); // TODO mb Hostile
            }
        }
    }

    /// <summary>
    /// Distributes the items, xp and coins that are currently in the Rewards Pool and resets it
    /// Mails items if not enough space to add all items directly added to inventory
    /// </summary>
    /// <returns></returns>
    public bool DistributeRewards(bool addBaseQuestReward)
    {
        var res = true;
        // Distribute Items if needed
        if ((QuestRewardItemsPool.Count > 0) && (AllowItemRewards))
        {
            // TODO: Add a way to distribute honor or vocation badges in mail as well
            if (Owner.Inventory.Bag.FreeSlotCount < QuestRewardItemsPool.Count)
            {
                var mails = MailManager.CreateQuestRewardMails(Owner, this, QuestRewardItemsPool, QuestRewardCoinsPool);
                QuestRewardCoinsPool = 0; // Coins will be distributed in mail if any mail needed to be sent, so set to zero again
                foreach (var mail in mails)
                    if (!mail.Send())
                    {
                        Owner.SendErrorMessage(ErrorMessageType.MailUnknownFailure);
                        res = false;
                    }

                Owner.SendPacket(new SCQuestRewardedByMailPacket(new uint[] { TemplateId }));
            }
            else
            {
                var pool = QuestRewardItemsPool.ToList();
                foreach (var item in pool)
                {
                    if (ItemManager.Instance.IsAutoEquipTradePack(item.TemplateId))
                    {
                        Owner.Inventory.TryEquipNewBackPack(ItemTaskType.QuestSupplyItems, item.TemplateId, item.Count, item.GradeId);
                    }
                    else
                    {
                        Owner.Inventory.Bag.AcquireDefaultItem(ItemTaskType.QuestSupplyItems, item.TemplateId, item.Count, item.GradeId);
                    }
                }
            }

            QuestRewardItemsPool.Clear();
        }

        // Add quest level based rewards
        if ((addBaseQuestReward) && (Template.Level > 0) && (Step == QuestComponentKind.Reward))
        {
            var levelBasedRewards = QuestManager.Instance.GetSupplies(Template.Level);
            if (levelBasedRewards != null)
            {
                // Add (or reduce) extra for over-achieving (or early completing) of the quest if allowed
                if (Template.LetItDone)
                {
                    var objectiveStatus = GetQuestObjectiveStatus();
                    switch (objectiveStatus)
                    {
                        case QuestObjectiveStatus.NotReady:
                            QuestRewardRatio = 0f;
                            break;
                        case QuestObjectiveStatus.CanEarlyComplete:
                            QuestRewardRatio = 0.3f;
                            break;
                        case QuestObjectiveStatus.QuestComplete:
                            QuestRewardRatio = 1f;
                            break;
                        case QuestObjectiveStatus.ExtraProgress:
                            QuestRewardRatio = 1f;
                            break;
                        case QuestObjectiveStatus.Overachieved:
                            QuestRewardRatio = 1.2f;
                            break;
                        default:
                            QuestRewardRatio = 1f;
                            break;
                    }
                }
                else
                {
                    QuestRewardRatio = 1f;
                }

                QuestRewardExpPool += levelBasedRewards.Exp;
                QuestRewardCoinsPool += levelBasedRewards.Copper;
            }
        }

        // Add XP
        if (QuestRewardExpPool > 0)
        {
            var xp = (int)Math.Round(QuestRewardExpPool * QuestRewardRatio);
            if (xp > 0)
                Owner.AddExp(xp, true);
            QuestRewardExpPool = 0;
        }

        // Add copper coins
        if (QuestRewardCoinsPool > 0)
        {
            var copper = (int)Math.Round(QuestRewardCoinsPool * QuestRewardRatio);
            if (copper > 0)
                Owner.ChangeMoney(SlotType.Invalid, SlotType.Bag, copper);
            QuestRewardCoinsPool = 0;
        }

        // Cleanup used Items from quest
        if (QuestCleanupItemsPool.Count > 0)
        {
            var cleanupList = QuestCleanupItemsPool.ToList();
            foreach (var cleanupItem in cleanupList)
                Owner.Inventory.ConsumeItem(null, ItemTaskType.QuestComplete, cleanupItem.TemplateId, cleanupItem.Count, null);
            QuestCleanupItemsPool.Clear();
        }

        return res;
    }

    /// <summary>
    /// Drops the quest as a result of the player requesting it
    /// </summary>
    /// <param name="update">Should an update packet be sent to the player</param>
    public void Drop(bool update)
    {
        Status = QuestStatus.Dropped;
        Step = QuestComponentKind.Drop;

        for (var step = QuestComponentKind.Ready; step < QuestComponentKind.Reward; step++)
        {
            var component = Template.GetFirstComponent(step);
            if (component != null)
            {
                UseSkill(component);
                UseBuff(component);
            }
        }

        if (update)
            Owner.SendPacket(new SCQuestContextUpdatedPacket(this, 0));

        foreach (var questComponentTemplate in Template.Components.Values)
            foreach (var actTemplate in questComponentTemplate.ActTemplates)
                actTemplate.QuestDropped(this);

        ClearObjectives();
    }

    /// <summary>
    /// Resets objectives
    /// </summary>
    private void ClearObjectives()
    {
        Objectives = new int[MaxObjectiveCount];
        RequestEvaluation();
    }

    /// <summary>
    /// Helper function for /quest GM command
    /// </summary>
    /// <param name="step"></param>
    /// <returns></returns>
    public int[] GetObjectives(QuestComponentKind step)
    {
        return Objectives;
    }

    /// <summary>
    /// Runs the QuestCleanup code of all the quest's acts
    /// </summary>
    public void Cleanup()
    {
        foreach (var questComponentTemplate in Template.Components.Values)
        {
            foreach (var actTemplate in questComponentTemplate.ActTemplates)
            {
                actTemplate.QuestCleanup(this);
            }
        }
    }

    /// <summary>
    /// Sets the RequestEvaluationFlag to true signalling the server that it should check this quest's progress again
    /// </summary>
    public void RequestEvaluation()
    {
        RequestEvaluationFlag = true;
    }

    /// <summary>
    /// Runs initializers for Acts that need to be activated at the start of the quest
    /// </summary>
    public void InitializeQuestActs()
    {
        foreach (var questStep in QuestSteps.Values)
        {
            foreach (var questComponent in questStep.Components.Values)
            {
                foreach (var questAct in questComponent.Acts)
                {
                    questAct.Template.InitializeQuest(this, questAct);
                }
            }
        }
    }

    /// <summary>
    /// Runs Finalizers for Acts that are active the entire quest
    /// </summary>
    public void FinalizeQuestActs()
    {
        foreach (var questStep in QuestSteps.Values)
        {
            foreach (var questComponent in questStep.Components.Values)
            {
                foreach (var questAct in questComponent.Acts)
                {
                    questAct.Template.FinalizeQuest(this, questAct);
                }
            }
        }
    }

    /// <summary>
    /// Called at the End of Quest creation to enable the ability to request evaluations
    /// If previously already request, it gets added to the queue here
    /// </summary>
    public void QuestInitialized()
    {
        _questInitializationFinished = true;
        if (_requestEvaluationFlag)
            _questManager.EnqueueEvaluation(this);
    }

    #region Packets and Database

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(Id);
        stream.Write(TemplateId);
        stream.Write((byte)Status);

        // TODO do-while, count 5
        stream.WritePiscW(MaxObjectiveCount, Objectives.Select(intValue => (long)intValue).ToArray());

        stream.Write(IsCheckSet);     // isCheckSet
        stream.WriteBc((uint)ObjId);  // ObjId
        stream.Write(0u);             // type(id)
        stream.WriteBc((uint)ObjId);  // ObjId
        stream.WriteBc((uint)ObjId);  // ObjId
        stream.Write(LeftTime);       // quest time limit
        stream.Write(LeftTime == -1 ? 0 : ComponentId); // type(id) - indicates which step is limited
        stream.Write(DoodadId);                // doodadId
        stream.Write(DateTime.UtcNow);         // acceptTime
        stream.Write((byte)QuestAcceptorType); // type QuestAcceptorType
        stream.Write(AcceptorId);              // acceptorType npcId or doodadId

        return stream;
    }

    public void ReadData(byte[] data)
    {
        var stream = new PacketStream(data);
        // Read Objectives
        var newObjectives = new int[MaxObjectiveCount];
        for (var i = 0; i < MaxObjectiveCount; i++)
        {
            newObjectives[i] = stream.ReadInt32();
        }
        // Read Current Step
        Step = (QuestComponentKind)stream.ReadByte();
        // Reset objectives counts only after setting the step, or they will reset
        Objectives = newObjectives.Select(intValue => intValue).ToArray();
        QuestAcceptorType = (QuestAcceptorType)stream.ReadByte();
        ComponentId = stream.ReadUInt32();
        AcceptorId = stream.ReadUInt32();
        Time = stream.ReadDateTime();
    }

    public byte[] WriteData()
    {
        var stream = new PacketStream();
        foreach (var objective in Objectives)
        {
            stream.Write(objective);
        }
        stream.Write((byte)Step);
        stream.Write((byte)QuestAcceptorType);
        stream.Write(ComponentId);
        stream.Write(AcceptorId);
        stream.Write(Time);
        return stream.GetBytes();
    }

    #endregion
}
