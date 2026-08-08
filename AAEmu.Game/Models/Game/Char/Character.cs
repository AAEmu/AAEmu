using System.Collections.Concurrent;
using System.Data;
using System.Drawing;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Commons.Utils.DB;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.SkillControllers;
using AAEmu.Game.Models.Game.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Utils;

using MySql.Data.MySqlClient;

using Task = System.Threading.Tasks.Task;

#pragma warning disable IDE0079 // Remove unnecessary suppression

namespace AAEmu.Game.Models.Game.Char;

public partial class Character : Unit, ICharacter
{
    public override UnitTypeFlag TypeFlag { get => UnitTypeFlag.Character; }
    public override BaseUnitType BaseUnitType => BaseUnitType.Character;

    public static Dictionary<uint, uint> UsedCharacterObjIds { get; } = [];

    /// <summary>
    /// Zone-mirror NPCs waiting for SCUnitState — queued while loading, outside soft AOI, or at MAX.
    /// </summary>
    private readonly ConcurrentDictionary<uint, Npc> _pendingMirrorSpawns = new();

    /// <summary>
    /// ObjIds that already received SCUnitState and still count toward AAEMU_MIRROR_NPC_MAX.
    /// Freed on leave-view so walking recycles slots (lifetime counter Quit'd after first N).
    /// </summary>
    public ConcurrentDictionary<uint, byte> MirrorNpcStatesSentIds { get; } = new();

    /// <summary>In-view streamed mirror count (for cap checks).</summary>
    public int MirrorNpcStatesSentCount => MirrorNpcStatesSentIds.Count;

    /// <summary>True after NotifyInGameCompleted — never send mirror UnitState during select/load.</summary>
    public bool MirrorNpcStreamReady { get; set; }

    /// <summary>
    /// Optional delay after Completed before first mirror UnitState (AAEMU_MIRROR_NPC_GRACE_MS).
    /// </summary>
    public long MirrorNpcStreamNotBeforeTick { get; set; }

    public void ResetMirrorNpcStreaming()
    {
        MirrorNpcStreamReady = false;
        MirrorNpcStreamNotBeforeTick = 0;
        MirrorNpcStatesSentIds.Clear();
        _pendingMirrorSpawns.Clear();
    }

    /// <summary>Arm mirror interest after load complete (+ optional grace ms).</summary>
    public void ArmMirrorNpcStream(int graceMs = 0)
    {
        MirrorNpcStreamReady = true;
        MirrorNpcStreamNotBeforeTick = Environment.TickCount64 + Math.Max(0, graceMs);
    }

    /// <summary>
    /// </summary>
    public bool CanStreamMirrorNow(Npc npc)
    {
        if (npc == null || npc.ObjId == 0)
            return false;
        if (!MirrorNpcStreamReady)
            return false;
        if (MirrorNpcStreamNotBeforeTick != 0 &&
            Environment.TickCount64 < MirrorNpcStreamNotBeforeTick)
            return false;
        if (MirrorNpcStatesSentIds.ContainsKey(npc.ObjId))
            return false;
        if (Npc.MirrorNpcMaxPerCharacter > 0 &&
            MirrorNpcStatesSentCount >= Npc.MirrorNpcMaxPerCharacter)
            return false;
        var d2 = DistanceSq(Transform.World.Position, npc.Transform.World.Position);
        return d2 <= Npc.MirrorNpcAoiRadiusSq;
    }

    /// <summary>Queue a zone mirror for later AOI enter / post-load flush.</summary>
    public void EnqueuePendingMirrorSpawn(Npc npc)
    {
        if (npc == null || npc.ObjId == 0)
            return;
        if (MirrorNpcStatesSentIds.ContainsKey(npc.ObjId))
            return;
        _pendingMirrorSpawns.TryAdd(npc.ObjId, npc);
    }

    public void ReleaseMirrorNpcSlot(uint objId)
    {
        MirrorNpcStatesSentIds.TryRemove(objId, out _);
        _pendingMirrorSpawns.TryRemove(objId, out _);
    }

    public bool HasPendingMirrorSpawns => !_pendingMirrorSpawns.IsEmpty;

    /// <summary>Remove and return the nearest pending valid mirror, or null.</summary>
    public Npc TryTakeNearestPendingMirror()
    {
        if (!TryPeekNearestPendingMirror(out var best, out _, out var bestId) || best == null)
            return null;

        _pendingMirrorSpawns.TryRemove(bestId, out _);
        return best;
    }

    /// <summary>Peek nearest pending inside soft AOI without removing. Drops dead pending entries.</summary>
    public bool TryPeekNearestPendingMirror(out Npc best, out float bestD2, out uint bestId)
    {
        best = null;
        bestD2 = float.MaxValue;
        bestId = 0;
        var origin = Transform.World.Position;

        foreach (var kv in _pendingMirrorSpawns)
        {
            var npc = kv.Value;
            if (npc == null || npc.ObjId == 0 || npc.Region == null || !npc.IsVisible)
            {
                _pendingMirrorSpawns.TryRemove(kv.Key, out _);
                continue;
            }

            if (MirrorNpcStatesSentIds.ContainsKey(npc.ObjId))
            {
                _pendingMirrorSpawns.TryRemove(kv.Key, out _);
                continue;
            }

            var d2 = DistanceSq(origin, npc.Transform.World.Position);
            // Outside soft AOI: skip for send (still pending for when player walks closer).
            if (d2 > Npc.MirrorNpcAoiRadiusSq)
                continue;

            if (d2 < bestD2)
            {
                bestD2 = d2;
                best = npc;
                bestId = kv.Key;
            }
        }

        return best != null;
    }

    private static float DistanceSq(System.Numerics.Vector3 a, System.Numerics.Vector3 b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }

    private bool IsStillInRegionInterest(Npc npc)
    {
        if (npc?.Region == null || Region == null)
            return false;
        if (ReferenceEquals(npc.Region, Region))
            return true;
        foreach (var n in Region.GetNeighbors())
        {
            if (ReferenceEquals(n, npc.Region))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Commercial interest leave: despawn streamed mirrors beyond soft AOI even if still in
    /// coarse region neighborhood. Re-queues those still in the region pool as pending.
    /// </summary>
    public int CullStreamedMirrorsBeyondAoi()
    {
        if (MirrorNpcStatesSentIds.IsEmpty)
            return 0;

        var origin = Transform.World.Position;
        var aoiSq = Npc.MirrorNpcAoiRadiusSq;
        List<uint> remove = null;

        foreach (var objId in MirrorNpcStatesSentIds.Keys)
        {
            var npc = ParentWorld?.GetNpc(objId);
            if (npc == null || npc.ObjId == 0)
            {
                (remove ??= []).Add(objId);
                continue;
            }

            if (DistanceSq(origin, npc.Transform.World.Position) > aoiSq)
                (remove ??= []).Add(objId);
        }

        if (remove == null || remove.Count == 0)
            return 0;

        foreach (var objId in remove)
        {
            var npc = ParentWorld?.GetNpc(objId);
            ReleaseMirrorNpcSlot(objId);
            if (npc != null && IsStillInRegionInterest(npc))
                EnqueuePendingMirrorSpawn(npc);
        }

        for (var offset = 0; offset < remove.Count; offset += SCUnitsRemovedPacket.MaxCountPerPacket)
        {
            var length = Math.Min(SCUnitsRemovedPacket.MaxCountPerPacket, remove.Count - offset);
            var batch = new uint[length];
            remove.CopyTo(offset, batch, 0, length);
            SendPacket(new SCUnitsRemovedPacket(batch));
        }

        return remove.Count;
    }

    /// <summary>
    /// At MAX: if a nearer pending exists inside AOI, despawn farthest streamed and free a slot
    /// </summary>
    public bool TryEvictFarthestStreamedForNearerPending()
    {
        if (Npc.MirrorNpcMaxPerCharacter <= 0)
            return false;
        if (MirrorNpcStatesSentCount < Npc.MirrorNpcMaxPerCharacter)
            return false;
        if (!TryPeekNearestPendingMirror(out _, out var nearerD2, out _))
            return false;

        var origin = Transform.World.Position;
        uint farthestId = 0;
        var farthestD2 = -1f;
        Npc farthestNpc = null;

        foreach (var objId in MirrorNpcStatesSentIds.Keys)
        {
            var npc = ParentWorld?.GetNpc(objId);
            if (npc == null)
            {
                ReleaseMirrorNpcSlot(objId);
                continue;
            }

            var d2 = DistanceSq(origin, npc.Transform.World.Position);
            if (d2 > farthestD2)
            {
                farthestD2 = d2;
                farthestId = objId;
                farthestNpc = npc;
            }
        }

        if (farthestId == 0 || nearerD2 >= farthestD2)
            return false;

        // Require meaningful improvement (~15m closer) to avoid thrash.
        const float minImproveSq = 15f * 15f;
        if (farthestD2 - nearerD2 < minImproveSq)
            return false;

        ReleaseMirrorNpcSlot(farthestId);
        if (farthestNpc != null && IsStillInRegionInterest(farthestNpc))
            EnqueuePendingMirrorSpawn(farthestNpc);
        SendPacket(new SCUnitsRemovedPacket([farthestId]));
        return true;
    }

    /// <summary>
    /// Physics-time (tPhy) anchor reconstructed from the client's own CSMoveUnit.Time. The 10.0.2.13 client
    /// binds and interpolates world objects against the server physics clock carried in movement packets, so
    /// the synthesized NPC keepalive movements (MirrorMovementStreamTask) MUST carry a tPhy in the client's
    /// </summary>
    public uint PhysTimeAnchor { get; set; }
    public long PhysTimeAnchorTick { get; set; }
    public bool HasPhysTimeAnchor => PhysTimeAnchorTick != 0;
    public uint CurrentPhysTime => PhysTimeAnchor + (uint)(Environment.TickCount64 - PhysTimeAnchorTick);

    private readonly Dictionary<ushort, string> _options;

    public List<IDisposable> Subscribers { get; set; }
    public override CharacterEvents Events { get; } = new();
    //public uint Id { get; set; } // moved to BaseUnit
    public uint AccountId { get; set; }
    public Race Race { get; set; }
    public Gender Gender { get; set; }
    /// <summary>
    /// The ServerId this character exists on
    /// </summary>
    public uint ServerId { get; set; }

    /// <summary>
    /// Cached representation of Account Labor
    /// </summary>
    public short LaborPower
    {
        get => _laborPower;
        set
        {
            if (_laborPower == value)
                return;
            _laborPower = value;
            AccountManager.Instance.UpdateLabor(AccountId, value);
        }
    }

    /// <summary>
    /// </summary>
    public int LocalLaborPower { get; set; }

    public int MaxLocalLaborPower => Math.Max(
        0,
        PremiumGameData.Instance.GetGrade(PremiumGrade)?.MaxLocalLabor ?? 0);

    /// <summary>
    /// Last time labor got updated
    /// </summary>
    public DateTime LaborPowerModified
    {
        get => _laborPowerModified;
        set
        {
            if (_laborPowerModified == value)
                return;

            _laborPowerModified = value;
            AccountManager.Instance.UpdateTickTimes(AccountId, value, true, false, false);
        }
    }

    public int ConsumedLaborPower { get; set; }
    public AbilityType Ability1 { get; set; }
    public AbilityType Ability2 { get; set; }
    public AbilityType Ability3 { get; set; }
    public DateTime LastCast { get; set; }
    //public bool IsInCombat { get; set; } // there's already an isInBattle
    public bool IsInPostCast { get; set; }
    public bool IgnoreSkillCooldowns { get; set; }
    public string FactionName { get; set; }
    public string OriginFactionName { get; set; }
    public uint Family { get; set; }
    public short DeadCount { get; set; }
    public DateTime DeadTime { get; set; }
    public int RezWaitDuration { get; set; }
    public DateTime RezTime { get; set; }
    public int RezPenaltyDuration { get; set; }
    public DateTime LeaveTime { get; set; }
    public long Money { get; set; }
    public long Money2 { get; set; }
    public long AaPoint { get; set; }
    public long BankAaPoint { get; set; }
    public int HonorPoint { get; set; }
    public int VocationPoint { get; set; }

    /// <summary>
    /// Body to restore when a CharTransformEffect polymorph ends. Set on the first transform only, so a
    /// second one applied on top cannot overwrite the original and leave the player stuck in a borrowed model.
    /// Deliberately not persisted — a transform does not survive a relog.
    /// </summary>
    public uint? PreTransformModelId { get; set; }

    /// <summary>
    /// Current crime points (/50)
    /// </summary>
    public short CrimePoint
    {
        get;
        set
        {
            if (value != field)
            {
                field = value;
                CheckWantedThreshold();
            }
        }
    }
    /// <summary>
    /// Total infamy
    /// </summary>
    public int CrimeRecord {
        get;
        set
        {
            if (value != field)
            {
                field = value;
                CheckWantedThreshold();
            }
        }
    }
    public int JuryPoint { get; set; }
    public DateTime DeleteRequestTime { get; set; }
    public DateTime TransferRequestTime { get; set; }
    public DateTime DeleteTime { get; set; }

    /// <summary>
    /// Cache value of AccountDetails.Loyalty
    /// </summary>
    public long BmPoint { get; set; }
    public bool AutoUseAAPoint { get; set; }
    public CharacterPrivacyStatus PrivacyStatus { get; set; }
    public int PrevPoint { get; set; }
    public int Point { get; set; }

    public byte DuelTeamType { get; set; } = 0xFF;

    /// <summary>UnitState camp — faction-war camp assignment, not implemented.</summary>
    public byte Camp { get; set; }

    /// <summary>
    /// Premium grade resolved from <see cref="Point"/> against premium_grades. 0 is no premium.
    /// </summary>
    public uint PremiumGrade => PremiumGameData.Instance.GetGradeForPoint(Point);

    /// <summary>Cumulative heir exp (characters.heir_exp). heir_levels measures against the total.</summary>
    public long HeirExp { get; set; }

    /// <summary>
    /// Resolved from <see cref="HeirExp"/>, so the level and the total can never disagree.
    /// </summary>
    public override byte HeirLevel
    {
        get => HeirGameData.Instance.GetLevelForExp(HeirExp);
        set { /* derived from HeirExp */ }
    }

    /// <summary>Heir step for the current level; heir_skills is keyed on it.</summary>
    public byte HeirStep => HeirGameData.Instance.GetStepForLevel(HeirLevel);
    public int Gift { get; set; }
    public int Experience { get; private set; }
    public int RecoverableExp { get; set; }
    public DateTime Created { get; set; } // время создания персонажа

    /// <summary>
    /// Seconds this character has been played, across every session. Reported by SCPlayerGameData, whose
    /// 10.0.2.13 serializer names the field totalPlayTime.
    /// </summary>
    public uint TotalPlayTime { get; set; }

    /// <summary>When the current session began, so the running total includes time not yet persisted.</summary>
    private DateTime _sessionStartedAt = DateTime.UtcNow;

    /// <summary>Serializes empty-body heir level-up requests for this character.</summary>
    private readonly object _heirLevelUpLock = new();

    /// <summary>Persisted total plus the time accumulated since this session started.</summary>
    public uint GetTotalPlayTimeSeconds()
    {
        var elapsed = (DateTime.UtcNow - _sessionStartedAt).TotalSeconds;
        return TotalPlayTime + (uint)Math.Max(0, elapsed);
    }

    /// <summary>
    /// Folds the current session into the stored total and restarts the session clock, so a save part-way
    /// through a session cannot count the same seconds twice on the next one.
    /// </summary>
    public void AccumulatePlayTime()
    {
        TotalPlayTime = GetTotalPlayTimeSeconds();
        _sessionStartedAt = DateTime.UtcNow;
    }
    public DateTime Updated { get; set; } // время внесения изменений

    public uint ReturnDistrictId { get; set; }
    public uint ResurrectionDistrictId { get; set; }

    public override float Scale => 1f;
    public override byte RaceGender => (byte)(16 * (byte)Gender + (byte)Race);

    public CharacterVisualOptions VisualOptions { get; set; }

    public const int MaxActionSlots = 85;
    public ActionSlot[] Slots { get; set; }
    public Inventory Inventory { get; set; }
    public byte NumInventorySlots { get; set; }
    public short NumBankSlots { get; set; }

    // public Item[] BuyBack { get; set; }
    public ItemContainer BuyBackItems { get; set; }
    public BondDoodad Bonding { get; set; }
    public CharacterQuests Quests { get; set; }
    public CharacterMails Mails { get; set; }
    public CharacterAppellations Appellations { get; set; }
    public CharacterAbilities Abilities { get; set; }
    public CharacterPortals Portals { get; set; }
    public CharacterFriends Friends { get; set; }
    public CharacterBlocked Blocked { get; set; }
    public CharacterFavoriteCrafts FavoriteCrafts { get; set; }
    public CharacterMates Mates { get; set; }

    public byte ExpandedExpert { get; set; }
    public CharacterActability Actability { get; set; }

    public CharacterSkills Skills { get; set; }
    public CharacterHeirSkills HeirSkills { get; set; }
    public CharacterSkillActiveTypes SkillActiveTypes { get; set; }
    public CharacterCraft Craft { get; set; }
    public uint SubZoneId { get; set; } // понадобилось хранить для составления точек Memory Tome (Recall)
    public int AccessLevel { get; set; }
    public WorldSpawnPosition LocalPingPosition { get; set; } // added as a GM command helper
    private ConcurrentDictionary<uint, DateTime> _hostilePlayers { get; set; }
    public bool IsRiding { get; set; }
    public bool SkillCancelled { get; set; }
    /// <summary>
    /// AttachPoint the player currently has in use  
    /// </summary>
    public AttachPointKind AttachedPoint { get; set; }

    /// <summary>
    /// Helper to keep track of what cinema is supposed to play
    /// </summary>
    public uint CurrentlyPlayingCinemaId { get; set; }

    /// <summary>
    /// Current instant game (arena/battlefield) the character is in
    /// </summary>
    public InstantGame.InstantGame CurrentInstantGame { get; set; }

    public override bool IsUnderWater
    {
        get { return _isUnderWater; }
        set
        {
            if (_isUnderWater == value) return;
            _isUnderWater = value;
            if (!_isUnderWater)
                Breath = LungCapacity;
            SendPacket(new SCUnderWaterPacket(_isUnderWater));
        }
    }

    private bool _inParty;
    private bool _isOnline;
    private short _laborPower;
    private DateTime _laborPowerModified;

    /// <summary>
    /// List of ObjIds you have aggro on
    /// </summary>
    public Dictionary<uint, BaseUnit> IsInAggroListOf { get; set; } = [];
    /// <summary>
    /// List of PlayerId's that have assaulted this player (either directly or indirectly)
    /// </summary>
    public List<uint> AssaultedBy { get; } = [];
    public List<uint> AssaultOn { get; } = [];

    public void InitializeLaborCache(short labor, DateTime newTime)
    {
        _laborPower = labor;
        _laborPowerModified = newTime;
    }

    public bool InParty
    {
        get => _inParty;
        set
        {
            if (_inParty == value) return;
            // TODO - GUILD STATUS CHANGE
            FriendMananger.Instance.SendStatusChange(this, false, value);
            _inParty = value;
        }
    }

    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            if (_isOnline == value) return;
            // TODO - GUILD STATUS CHANGE
            FriendMananger.Instance.SendStatusChange(this, true, value);
            if (!value) TeamManager.Instance.SetOffline(this);
            _isOnline = value;
        }
    }
    // public FishSchool FishSchool { get; set; }

    // Set to true when character has finished loading for this instance
    private bool FinishedLoading { get; set; }
    private int _savedHp = 99999999;
    private int _savedMp = 99999999;

    #region Attributes

    [UnitAttribute(UnitAttribute.GlobalCooldownMul)]
    public override float GlobalCooldownMul
    {
        get
        {
            var res = CalculateWithBonuses(0, UnitAttribute.GlobalCooldownMul);

            return (int)(100000f / (res + 1000f));
        }
    }

    [UnitAttribute(UnitAttribute.Str)]
    public int Str
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Str);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            var result = formula.Evaluate(parameters);
            var res = result;
            foreach (var item in Equipment.Items)
                if (item is EquipItem equip)
                    res += equip.Str;
            res = CalculateWithBonuses(res, UnitAttribute.Str);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.Dex)]
    public int Dex
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Dex);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            var res = formula.Evaluate(parameters);
            foreach (var item in Equipment.Items)
                if (item is EquipItem equip)
                    res += equip.Dex;
            res = CalculateWithBonuses(res, UnitAttribute.Dex);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.Sta)]
    public int Sta
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Sta);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            var res = formula.Evaluate(parameters);
            foreach (var item in Equipment.Items)
                if (item is EquipItem equip)
                    res += equip.Sta;
            res = CalculateWithBonuses(res, UnitAttribute.Sta);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.Int)]
    public int Int
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Int);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            var res = formula.Evaluate(parameters);
            foreach (var item in Equipment.Items)
                if (item is EquipItem equip)
                    res += equip.Int;
            res = CalculateWithBonuses(res, UnitAttribute.Int);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.Spi)]
    public int Spi
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Spi);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            var res = formula.Evaluate(parameters);
            foreach (var item in Equipment.Items)
                if (item is EquipItem equip)
                    res += equip.Spi;
            res = CalculateWithBonuses(res, UnitAttribute.Spi);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.Fai)]
    public int Fai
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Fai);
            var parameters = new Dictionary<string, double> { ["level"] = Level };
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.Fai);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.MaxHealth)]
    public override int MaxHp
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.MaxHealth);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.MaxHealth);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.HealthRegen)]
    public override int HpRegen
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.HealthRegen);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = formula.Evaluate(parameters);
            // res += Spi / 10;
            res = CalculateWithBonuses(res, UnitAttribute.HealthRegen);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.PersistentHealthRegen)]
    public override int PersistentHpRegen
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character,
                UnitFormulaKind.PersistentHealthRegen);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.PersistentHealthRegen);
            res /= 5;

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.MaxMana)]
    public override int MaxMp
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.MaxMana);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.MaxMana);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.ManaRegen)]
    public override int MpRegen
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.ManaRegen);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = formula.Evaluate(parameters);
            res += Spi / 10.0;
            res = CalculateWithBonuses(res, UnitAttribute.ManaRegen);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.PersistentManaRegen)]
    public override int PersistentMpRegen
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character,
                UnitFormulaKind.PersistentManaRegen);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = formula.Evaluate(parameters);
            res /= 5; // TODO ...
            res = CalculateWithBonuses(res, UnitAttribute.PersistentManaRegen);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.IncomingDamageMul)]
    public override float IncomingDamageMul
    {
        get
        {
            var res = 0d;
            res = CalculateWithBonuses(res, UnitAttribute.IncomingDamageMul);
            res = res / 1000;
            res = 1 + res;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.IncomingMeleeDamageMul)]
    public override float IncomingMeleeDamageMul
    {
        get
        {
            var res = 0d;
            res = CalculateWithBonuses(res, UnitAttribute.IncomingMeleeDamageMul);
            res = CalculateWithBonuses(res, UnitAttribute.IncomingDamageMul);
            res = res / 1000;
            res = 1 + res;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.IncomingRangedDamageMul)]
    public override float IncomingRangedDamageMul
    {
        get
        {
            var res = 0d;
            res = CalculateWithBonuses(res, UnitAttribute.IncomingRangedDamageMul);
            res = CalculateWithBonuses(res, UnitAttribute.IncomingDamageMul);
            res = res / 1000;
            res = 1 + res;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.IncomingSpellDamageMul)]
    public override float IncomingSpellDamageMul
    {
        get
        {
            var res = 0d;
            res = CalculateWithBonuses(res, UnitAttribute.IncomingSpellDamageMul);
            res = CalculateWithBonuses(res, UnitAttribute.IncomingDamageMul);
            res = res / 1000;
            res = 1 + res;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.CastingTimeMul)]
    public override float CastTimeMul
    {
        get
        {
            var res = 0d;
            res = CalculateWithBonuses(res, UnitAttribute.CastingTimeMul);
            res = (res + 1000.00000000) / 1000;
            return (float)Math.Max(res, 0f);
        }
    }

    [UnitAttribute(UnitAttribute.MeleeDamageMul)]
    public override float MeleeDamageMul
    {
        get
        {
            double res = 0f;
            res = CalculateWithBonuses(res, UnitAttribute.MeleeDamageMul);
            res = (res + 1000.00000000) / 1000;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.RangedDamageMul)]
    public override float RangedDamageMul
    {
        get
        {
            double res = 0f;
            res = CalculateWithBonuses(res, UnitAttribute.RangedDamageMul);
            res = (res + 1000.00000000) / 1000;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.SpellDamageMul)]
    public override float SpellDamageMul
    {
        get
        {
            double res = 0f;
            res = CalculateWithBonuses(res, UnitAttribute.SpellDamageMul);
            res = (res + 1000.00000000) / 1000;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.IncomingHealMul)]
    public override float IncomingHealMul
    {
        get
        {
            double res = 0f;
            res = CalculateWithBonuses(res, UnitAttribute.IncomingHealMul);
            res = (res + 1000.00000000) / 1000;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.HealMul)]
    public override float HealMul
    {
        get
        {
            double res = 0f;
            res = CalculateWithBonuses(res, UnitAttribute.HealMul);
            res = (res + 1000.00000000) / 1000;
            return (float)res;
        }
    }

    public override float LevelDps
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.LevelDps);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai,
                ["ab_level"] = Level // TODO : Make AbilityLevel
            };
            var res = formula.Evaluate(parameters);
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.MainhandDps)]
    public override int Dps
    {
        get
        {
            var weapon = (Weapon)Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Mainhand);
            var res = (weapon?.Dps ?? 0) * 1000f;
            res += Str / 5f * 1000f;
            res = (float)CalculateWithBonuses(res, UnitAttribute.MainhandDps);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.MeleeDpsInc)]
    public override int DpsInc
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.MeleeDpsInc);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.MeleeDpsInc);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.OffhandDps)]
    public override int OffhandDps
    {
        get
        {
            var weapon = (Weapon)Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Offhand);
            var res = weapon?.Dps ?? 0;
            // res += Str / 10f;
            res = (float)CalculateWithBonuses(res, UnitAttribute.OffhandDps);

            return (int)(res * 1000);
        }
    }

    [UnitAttribute(UnitAttribute.RangedDps)]
    public override int RangedDps
    {
        get
        {
            var weapon = (Weapon)Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Ranged);
            var res = (weapon?.Dps ?? 0) * 1000f;
            res += Dex / 5f * 1000f;
            res = (float)CalculateWithBonuses(res, UnitAttribute.RangedDps);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.RangedDpsInc)]
    public override int RangedDpsInc
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.RangedDpsInc);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.RangedDpsInc);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.SpellDps)]
    public override int MDps
    {
        get
        {
            var weapon = (Weapon)Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Mainhand);
            var res = (weapon?.MDps ?? 0) * 1000f;
            res += Int / 5f * 1000f;
            res = (float)CalculateWithBonuses(res, UnitAttribute.SpellDps);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.SpellDpsInc)]
    public override int MDpsInc
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.SpellDpsInc);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.SpellDpsInc);

            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.HealDps)]
    public override int HDps
    {
        get
        {
            var weapon = (Weapon)Inventory.Equipment.GetItemBySlot((int)EquipmentItemSlot.Mainhand);
            var res = (weapon?.HDps ?? 0) * 1000;
            res += Spi / 5f * 1000f;
            res = CalculateWithBonuses(res, UnitAttribute.HealDps);
            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.HealDpsInc)]
    public override int HDpsInc
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.HealDpsInc);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["spi"] = Spi
            };
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.HealDpsInc);
            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.MeleeAntiMiss)]
    public override float MeleeAccuracy
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.MeleeAntiMiss);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["heir_level"] = HeirLevel,
                ["str"] = Str, //Str not needed, but maybe we use later
                ["spi"] = Spi
            };
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.MeleeAntiMiss);
            res = (1f - (Facets / 10f - res) * (1f / Facets)) * 100f;
            res = (res + 100f - Math.Abs(res - 100f)) / 2f;
            res = (Math.Abs(res) + res) / 2f;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.MeleeCritical)]
    public override float MeleeCritical
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.MeleeCritical);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["heir_level"] = HeirLevel,
                ["str"] = Str, //Str not needed, but maybe we use later
                ["dex"] = Dex
            };
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.MeleeCritical);
            res = res * (1f / Facets) * 100;
            res = res + MeleeCriticalMul / 10;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.MeleeCriticalBonus)]
    public override float MeleeCriticalBonus
    {
        get
        {
            var res = 1500f;
            res = (float)CalculateWithBonuses(res, UnitAttribute.MeleeCriticalBonus);
            return (res - 1000f) / 10f;
        }
    }

    [UnitAttribute(UnitAttribute.MeleeCriticalMul)]
    public override float MeleeCriticalMul
    {
        get
        {
            float res = 0;
            res = (float)CalculateWithBonuses(res, UnitAttribute.MeleeCriticalMul);
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.RangedAntiMiss)]
    public override float RangedAccuracy
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.RangedAntiMiss);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["heir_level"] = HeirLevel,
                ["dex"] = Dex, //Str not needed, but maybe we use later
                ["spi"] = Spi
            };
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.RangedAntiMiss);
            res = (1f - (Facets / 10f - res) * (1f / Facets)) * 100f;
            res = (res + 100f - Math.Abs(res - 100f)) / 2f;
            res = (Math.Abs(res) + res) / 2f;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.RangedCritical)]
    public override float RangedCritical
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.RangedCritical);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["heir_level"] = HeirLevel,
                ["dex"] = Dex, //Str not needed, but maybe we use later
                ["int"] = Int
            };
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.RangedCritical);
            res = res * (1f / Facets) * 100;
            res = res + RangedCriticalMul / 10;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.RangedCriticalBonus)]
    public override float RangedCriticalBonus
    {
        get
        {
            var res = 1500f;
            res = (float)CalculateWithBonuses(res, UnitAttribute.RangedCriticalBonus);
            return (res - 1000f) / 10f;
        }
    }

    [UnitAttribute(UnitAttribute.RangedCriticalMul)]
    public override float RangedCriticalMul
    {
        get
        {
            float res = 0;
            res = (float)CalculateWithBonuses(res, UnitAttribute.RangedCriticalMul);
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.SpellAntiMiss)]
    public override float SpellAccuracy
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.SpellAntiMiss);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["heir_level"] = HeirLevel,
                ["int"] = Int,
                ["spi"] = Spi
            };
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.SpellAntiMiss);
            res = (1f - (Facets / 10f - res) * (1f / Facets)) * 100f;
            res = (res + 100f - Math.Abs(res - 100f)) / 2f;
            res = (Math.Abs(res) + res) / 2f;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.SpellCritical)]
    public override float SpellCritical
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.SpellCritical);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["heir_level"] = HeirLevel,
                ["int"] = Int //Str not needed, but maybe we use later
            };
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.SpellCritical);
            res = (float)CalculateWithBonuses(res, UnitAttribute.SpellDamageCritical);
            res = res * (1f / Facets) * 100;
            res = res + SpellCriticalMul / 10;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.SpellCriticalBonus)]
    public override float SpellCriticalBonus
    {
        get
        {
            var res = 1500f;
            res = (float)CalculateWithBonuses(res, UnitAttribute.SpellCriticalBonus);
            res = (float)CalculateWithBonuses(res, UnitAttribute.SpellDamageCriticalBonus);
            return (res - 1000f) / 10f;
        }
    }

    [UnitAttribute(UnitAttribute.SpellCriticalMul)]
    public override float SpellCriticalMul
    {
        get
        {
            double res = 0;
            res = CalculateWithBonuses(res, UnitAttribute.SpellCriticalMul);
            res = (float)CalculateWithBonuses(res, UnitAttribute.SpellDamageCriticalMul);
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.HealCritical)]
    public override float HealCritical
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.HealCritical);
            var parameters = new Dictionary<string, double>
            {
                ["heir_level"] = HeirLevel,
                ["spi"] = Spi
            };
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.HealCritical);
            res = res * (1f / Facets) * 100;
            res = res + HealCriticalMul / 10;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.HealCriticalBonus)]
    public override float HealCriticalBonus
    {
        get
        {
            var res = 1500f;
            res = (float)CalculateWithBonuses(res, UnitAttribute.HealCriticalBonus);
            return (res - 1000f) / 10f;
        }
    }

    [UnitAttribute(UnitAttribute.HealCriticalMul)]
    public override float HealCriticalMul
    {
        get
        {
            double res = 0;
            res = CalculateWithBonuses(res, UnitAttribute.HealCriticalMul);
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.Armor)]
    public override int Armor
    {
        get
        {
            var formula = FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Armor);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = (int)formula.Evaluate(parameters);
            foreach (var item in Equipment.Items)
            {
                switch (item)
                {
                    case Armor armor:
                        res += armor.BaseArmor;
                        break;
                    case Weapon weapon:
                        res += weapon.Armor;
                        break;
                    case Accessory accessory:
                        res += accessory.BaseArmor;
                        break;
                }
            }

            res = (int)CalculateWithBonuses(res, UnitAttribute.Armor);

            return res;
        }
    }

    [UnitAttribute(UnitAttribute.MagicResist)]
    public override int MagicResistance
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.MagicResist);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["str"] = Str,
                ["dex"] = Dex,
                ["sta"] = Sta,
                ["int"] = Int,
                ["spi"] = Spi,
                ["fai"] = Fai
            };
            var res = (int)formula.Evaluate(parameters);
            foreach (var item in Equipment.Items)
            {
                switch (item)
                {
                    case Armor armor:
                        res += armor.BaseMagicResistance;
                        break;
                    case Accessory accessory:
                        res += accessory.BaseMagicResistance;
                        break;
                }
            }

            res = (int)CalculateWithBonuses(res, UnitAttribute.MagicResist);

            return res;
        }
    }

    [UnitAttribute(UnitAttribute.IgnoreArmor)]
    public override int DefensePenetration
    {
        get
        {
            var res = CalculateWithBonuses(0, UnitAttribute.IgnoreArmor);
            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.MagicPenetration)]
    public override int MagicPenetration
    {
        get
        {
            var res = CalculateWithBonuses(0, UnitAttribute.MagicPenetration);
            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.BattleResist)]
    public override int BattleResist
    {
        get
        {
            var res = (int)CalculateWithBonuses(0, UnitAttribute.BattleResist);
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.BullsEye)]
    public override int BullsEye
    {
        get
        {
            var res = (int)CalculateWithBonuses(0, UnitAttribute.BullsEye);
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.Flexibility)]
    public override int Flexibility
    {
        get
        {
            var res = (int)CalculateWithBonuses(0, UnitAttribute.Flexibility);
            return res;
        }
    }

    [UnitAttribute(UnitAttribute.Facets)]
    public override int Facets
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Facet);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level
            };
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.Facets);
            return (int)res;
        }
    }

    [UnitAttribute(UnitAttribute.Dodge)]
    public override float DodgeRate
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Dodge);
            var parameters = new Dictionary<string, double>
            {
                ["heir_level"] = HeirLevel,
                ["dex"] = Dex,
                ["int"] = Int
            };
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.Dodge);
            res = res * (1f / Facets) * 100f;
            res += CalculateWithBonuses(0f, UnitAttribute.DodgeMul) / 10f;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.MeleeParry)]
    public override float MeleeParryRate
    {
        get
        {
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.MeleeParry);
            var parameters = new Dictionary<string, double>
            {
                ["level"] = Level,
                ["heir_level"] = HeirLevel,
                ["str"] = Str,
                ["sta"] = Sta
            };
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.MeleeParry);
            res = res * (1f / Facets) * 100f;
            res += CalculateWithBonuses(0f, UnitAttribute.MeleeParryMul) / 10f;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.RangedParry)]
    public override float RangedParryRate
    {
        get
        {
            //RangedParry Formula == 0
            double res = 0;
            res = CalculateWithBonuses(res, UnitAttribute.RangedParry);
            res = res * (1f / Facets) * 100f;
            res += CalculateWithBonuses(0f, UnitAttribute.RangedParryMul) / 10f;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.Block)]
    public override float BlockRate
    {
        get
        {
            var offhand = Equipment.GetItemBySlot((int)EquipmentItemSlot.Offhand);
            if (offhand != null && offhand.Template is WeaponTemplate template)
            {
                var slotId = (EquipmentItemSlotType)template.HoldableTemplate.SlotTypeId;
                if (slotId != EquipmentItemSlotType.Shield)
                    return 0f;
            }
            else if (offhand == null)
                return 0f;
            var formula =
                FormulaManager.Instance.GetUnitFormula(FormulaOwnerType.Character, UnitFormulaKind.Block);
            var parameters = new Dictionary<string, double>
            {
                ["heir_level"] = HeirLevel,
                ["str"] = Str,
                ["sta"] = Sta
            };
            var res = formula.Evaluate(parameters);
            res = CalculateWithBonuses(res, UnitAttribute.Block);
            res = res * (1f / Facets) * 100f;
            res += CalculateWithBonuses(0f, UnitAttribute.BlockMul) / 10f;
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.LungCapacity)]
    public uint LungCapacity
    {
        get => (uint)CalculateWithBonuses(60000, UnitAttribute.LungCapacity);
    }

    [UnitAttribute(UnitAttribute.FallDamageMul)]
    public float FallDamageMul
    {
        get => (float)CalculateWithBonuses(1d, UnitAttribute.FallDamageMul);
    }

    [UnitAttribute(UnitAttribute.LivingPointGain)]
    public float LivingPointGain
    {
        get
        {
            var res = 0.0;
            res = CalculateWithBonuses(res, UnitAttribute.LivingPointGain);
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.LivingPointGainMul)]
    public float LivingPointGainMul
    {
        get
        {
            var res = 0.0;
            res = CalculateWithBonuses(res, UnitAttribute.LivingPointGainMul);
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.DropRateMul)]
    public float DropRateMul
    {
        get
        {
            var res = 0.0;
            res = CalculateWithBonuses(res, UnitAttribute.DropRateMul);
            return (float)res;
        }
    }

    [UnitAttribute(UnitAttribute.LootGoldMul)]
    public float LootGoldMul
    {
        get
        {
            var res = 0.0;
            res = CalculateWithBonuses(res, UnitAttribute.LootGoldMul);
            return (float)res;
        }
    }

    #endregion

    /// <summary>
    /// This time is used to decide if a user lost connection
    /// </summary>
    public DateTime LastPacketActivityTime { get; set; } = DateTime.UtcNow;

    public Character(UnitCustomModelParams modelParams)
    {
        _options = [];
        _hostilePlayers = new ConcurrentDictionary<uint, DateTime>();
        Breath = LungCapacity;
        ModelParams = modelParams;
        Subscribers = [];
        ChargeLock = new object();
        // FishSchool = new FishSchool(this);
        //Events.OnDisconnect += OnDisconnect;
        //Events.OnCombatStarted += OnEnterCombat;
    }

    public void SetHostileActivity(Character attacker)
    {
        if (_hostilePlayers.ContainsKey(attacker.ObjId))
            _hostilePlayers[attacker.ObjId] = DateTime.UtcNow;
        else
            _hostilePlayers.TryAdd(attacker.ObjId, DateTime.UtcNow);
    }

    public bool IsActivelyHostile(Character target)
    {
        if (_hostilePlayers.TryGetValue(target.ObjId, out var value))
        {
            //Maybe get the time to stay hostile from db?
            return value.AddSeconds(WorldManager.DefaultCombatTimeout) > DateTime.UtcNow;
        }
        return false;
    }

    /// <summary>
    /// cumulative heir experience is exactly one below the current row's threshold; the server
    /// repeats every eligibility check because the request has no fields and cannot be trusted.
    /// </summary>
    public bool TryLevelUpHeir()
    {
        lock (_heirLevelUpLock)
        {
            var gameData = HeirGameData.Instance;
            if (!gameData.TryGetLevelUpRequirement(Level, HeirExp, out var requirement))
                return false;

            if (requirement.ReqItemId != 0)
            {
                if (!Inventory.CheckItems(SlotType.Inventory, requirement.ReqItemId, requirement.ReqItemCount))
                    return false;

                var consumed = Inventory.Bag.ConsumeItem(
                    ItemTaskType.UpgradeSkill,
                    requirement.ReqItemId,
                    requirement.ReqItemCount,
                    null);
                if (consumed != requirement.ReqItemCount)
                    return false;
            }

            // Crossing this content-supplied threshold makes the upper-bound lookup advance by one.
            HeirExp = requirement.ReqTotalExp;
            BroadcastPacket(new SCHeirLevelUpPacket(ObjId), true);
            return true;
        }
    }

    public void AddExp(int expDelta, bool shouldAddAbilityExp)
    {
        if (expDelta == 0)
            return;

        if (expDelta > 0)
        {
            expDelta = (int)(expDelta * AppConfiguration.Instance.World.ExpRate);
        }

        // level before SCLevelChanged arrives, and accepts positive deltas only. Levels that owe an
        // item clamp one point below their threshold and wait for CSHeirLevlUp; the rest are crossed
        // inside ApplyExpGain. Nothing accrues while the feature is off, so heir levels cannot build
        // up unseen and appear all at once if it is later switched on.
        var wasHeirEligible = FeaturesManager.HeirEnabled && Level >= HeirGameData.Instance.StartLevel;
        if (wasHeirEligible)
        {
            var previousHeirLevel = HeirLevel;
            HeirExp = HeirGameData.Instance.ApplyExpGain(HeirExp, expDelta);

            // SCHeirLevelUp carries no level value - the client increments its own heir level by
            // one per packet - so a gain spanning several free levels needs one packet each.
            for (var gained = previousHeirLevel; gained < HeirLevel; gained++)
                BroadcastPacket(new SCHeirLevelUpPacket(ObjId), true);
        }

        var newExperience = Experience + expDelta;
        var newLevel = ExperienceManager.Instance.GetLevelFromExp(newExperience, Level, out var overflow);
        var leveledUp = newLevel > Level;
        
        // Prevent overflow - cap the experience at the amount for the highest level
        if (newLevel >= ExperienceManager.Instance.MaxPlayerLevel)
        {
            newExperience -= overflow;
        }
        
        Experience = newExperience;
        Level = newLevel;
        
        // to the equipped abilities. Mirror that ordering before SCExpChanged reaches the client.
        if (shouldAddAbilityExp)
            Abilities.AddActiveExp(expDelta);
        
        SendPacket(new SCExpChangedPacket(ObjId, expDelta, shouldAddAbilityExp));

        if (leveledUp)
            ApplyLevelUpBenefits();
    }

    /// <summary>Refills level-dependent vitals and synchronizes the new level and unit state.</summary>
    private void ApplyLevelUpBenefits()
    {
        Expedition?.OnCharacterRefresh(this);

        // Level is already on this.Level; MaxHp/MaxMp getters re-evaluate immediately.
        Hp = MaxHp;
        Mp = MaxMp;

        BroadcastPacket(new SCLevelChangedPacket(ObjId, Level), true);
        // Re-push appearance/max for self so the local unit frame denominator matches the new level.
        // SCUnitPoints alone only patches current precise HP/MP (see CharacterMates mate gear path).
        SendPacket(new SCUnitStatePacket(this));
        BroadcastPacket(new SCUnitPointsPacket(ObjId, Hp, Mp), true);

        if (WorldIntegration.ZoneAuthority)
        {
            WorldIntegration.RelayLevelChangedToZone?.Invoke(ObjId, Level);
            WorldIntegration.RelayUnitPointsToZone?.Invoke(ObjId, Hp, Mp);
        }

        // AcceptLevelUp starters only make sense on an actual level change — firing on every
        // AddExp re-scans the whole table and amplified the Elf-spawn badge-quest cascade.
        if (Connection != null)
            QuestManager.Instance.DoOnLevelUpEvents(Connection.ActiveChar);

        Logger.Info("{0} leveled to {1}: hp={2}/{3} mp={4}/{5}", Name, Level, Hp, MaxHp, Mp, MaxMp);
    }

    private void ValidateAndFixExpAndLevel()
    {
        // Check if player has too little exp to be at their current level, and give them enough to maintain it
        var needExp = ExperienceManager.Instance.GetExpForLevel(Level);
        if (Experience < needExp)
        {
            Experience = needExp;
            return;
        }

        // Check if player has enough exp to be at a higher level, and grant them the new level
        var newLevel = ExperienceManager.Instance.GetLevelFromExp(Experience, Level, out var overflow);
        var leveledUp = newLevel > Level;
        
        // Prevent overflow - cap the experience at the amount for the highest level
        if (newLevel >= ExperienceManager.Instance.MaxPlayerLevel)
        {
            Experience -= overflow;
        }
        
        Level = newLevel;
        
        if (leveledUp)
            ApplyLevelUpBenefits();
    }

    public bool ChangeMoney(SlotType moneyLocation, long amount, ItemTaskType itemTaskType = ItemTaskType.DepositMoney)
    {
        if (amount > 0)
            return AddMoney(moneyLocation, amount, itemTaskType);
        if (amount < 0)
        {
            if (amount == long.MinValue)
                return false;
            return SubtractMoney(moneyLocation, -amount, itemTaskType);
        }
        return true;
    }

    public bool ChangeMoney(SlotType typeFrom, SlotType typeTo, long amount, ItemTaskType itemTaskType = ItemTaskType.DepositMoney)
    {
        return ChangeWallets(typeFrom, typeTo, amount, 0, itemTaskType);
    }

    public bool ChangeAAPoint(SlotType typeFrom, SlotType typeTo, long amount, ItemTaskType itemTaskType = ItemTaskType.DepositMoney)
    {
        return ChangeWallets(typeFrom, typeTo, 0, amount, itemTaskType);
    }

    public bool ChangeWallets(
        SlotType typeFrom,
        SlotType typeTo,
        long moneyAmount,
        long aaPointAmount,
        ItemTaskType itemTaskType = ItemTaskType.DepositMoney)
    {
        if (moneyAmount == 0 && aaPointAmount == 0)
            return true;

        if (moneyAmount < 0 || aaPointAmount < 0 ||
            (typeFrom == SlotType.None && typeTo == SlotType.None) || typeFrom == typeTo)
            return false;

        var sourceMoney = typeFrom switch
        {
            SlotType.None => long.MaxValue,
            SlotType.Inventory => Money,
            SlotType.Bank => Money2,
            _ => -1
        };
        var sourceAaPoint = typeFrom switch
        {
            SlotType.None => long.MaxValue,
            SlotType.Inventory => AaPoint,
            SlotType.Bank => BankAaPoint,
            _ => -1
        };
        if (sourceMoney < 0 || sourceAaPoint < 0)
            return false;
        if (sourceMoney < moneyAmount)
        {
            SendErrorMessage(typeFrom == SlotType.Bank
                ? ErrorMessageType.NotEnoughBankMoney
                : ErrorMessageType.NotEnoughMoney);
            return false;
        }
        if (sourceAaPoint < aaPointAmount)
        {
            SendErrorMessage(typeFrom == SlotType.Bank
                ? ErrorMessageType.NotEnoughBankAaPoint
                : ErrorMessageType.NotEnoughAaPoint);
            return false;
        }

        var targetMoney = typeTo switch
        {
            SlotType.None => 0,
            SlotType.Inventory => Money,
            SlotType.Bank => Money2,
            _ => -1
        };
        var targetAaPoint = typeTo switch
        {
            SlotType.None => 0,
            SlotType.Inventory => AaPoint,
            SlotType.Bank => BankAaPoint,
            _ => -1
        };
        if (targetMoney < 0 || targetAaPoint < 0 ||
            targetMoney > long.MaxValue - moneyAmount ||
            targetAaPoint > long.MaxValue - aaPointAmount)
        {
            SendErrorMessage(ErrorMessageType.Invalid);
            return false;
        }

        var itemTasks = new List<ItemTask>();
        switch (typeFrom)
        {
            case SlotType.Inventory:
                Money -= moneyAmount;
                AaPoint -= aaPointAmount;
                if (moneyAmount != 0)
                    itemTasks.Add(new MoneyChange(-moneyAmount));
                if (aaPointAmount != 0)
                    itemTasks.Add(new AAPointUpdate(-aaPointAmount));
                break;
            case SlotType.Bank:
                Money2 -= moneyAmount;
                BankAaPoint -= aaPointAmount;
                if (moneyAmount != 0)
                    itemTasks.Add(new MoneyChangeBank(-moneyAmount));
                if (aaPointAmount != 0)
                    itemTasks.Add(new ChangeBankAAPoint(-aaPointAmount));
                break;
        }
        switch (typeTo)
        {
            case SlotType.Inventory:
                Money += moneyAmount;
                AaPoint += aaPointAmount;
                if (moneyAmount != 0)
                    itemTasks.Add(new MoneyChange(moneyAmount));
                if (aaPointAmount != 0)
                    itemTasks.Add(new AAPointUpdate(aaPointAmount));
                break;
            case SlotType.Bank:
                Money2 += moneyAmount;
                BankAaPoint += aaPointAmount;
                if (moneyAmount != 0)
                    itemTasks.Add(new MoneyChangeBank(moneyAmount));
                if (aaPointAmount != 0)
                    itemTasks.Add(new ChangeBankAAPoint(aaPointAmount));
                break;
        }
        SendPacket(new SCItemTaskSuccessPacket(itemTaskType, itemTasks, []));
        return true;
    }

    public bool AddMoney(SlotType moneyLocation, long amount, ItemTaskType itemTaskType = ItemTaskType.DepositMoney)
    {
        if (amount < 0)
            return false;
        return ChangeMoney(SlotType.None, moneyLocation, amount, itemTaskType);
    }

    public bool SubtractMoney(SlotType moneyLocation, long amount, ItemTaskType itemTaskType = ItemTaskType.DepositMoney)
    {
        if (amount < 0)
            return false;
        return ChangeMoney(moneyLocation, SlotType.None, amount, itemTaskType);
    }

    public bool AddAAPoint(SlotType aaPointLocation, long amount, ItemTaskType itemTaskType = ItemTaskType.DepositMoney)
    {
        if (amount < 0)
            return false;
        return ChangeAAPoint(SlotType.None, aaPointLocation, amount, itemTaskType);
    }

    public bool SubtractAAPoint(SlotType aaPointLocation, long amount, ItemTaskType itemTaskType = ItemTaskType.DepositMoney)
    {
        if (amount < 0)
            return false;
        return ChangeAAPoint(aaPointLocation, SlotType.None, amount, itemTaskType);
    }

    public void ChangeLabor(short change, int actabilityId)
    {
        var actabilityChange = 0;
        byte actabilityStep = 0;
        var expMultiplier = 1f;
        if (actabilityId > 0)
        {
            // Get multiplier before adding points
            expMultiplier = Actability.Actabilities[(uint)actabilityId].GetExpMultiplier();
            actabilityChange = (int)(Math.Abs(change) * AppConfiguration.Instance.World.ActabilityRate);
            actabilityStep = Actability.Actabilities[(uint)actabilityId].Step;
            actabilityChange = Actability.AddPoint((uint)actabilityId, actabilityChange);
        }

        // Only grant xp if consuming labor
        if (change < 0)
        {
            var parameters = new Dictionary<string, double>
            {
                { "labor_power", -change },
                { "pc_level", Level }
            };
            var formula = FormulaManager.Instance.GetFormula((uint)FormulaKind.ExpByLaborPower);
            var xpToAdd = (int)(formula.Evaluate(parameters) * expMultiplier);
            AddExp(xpToAdd, true);
        }

        LaborPower += change;
        // amount = primary labor delta; local/recharged pools unused on our single-pool account labor.
        SendPacket(new SCCharacterLaborPowerChangedPacket(
            change, 0, 0, (uint)actabilityId, actabilityChange, actabilityStep));
    }

    /// <summary>
    /// premium-grade cap.
    /// </summary>
    public int AddLocalLaborPower(int amount)
    {
        if (amount <= 0)
            return 0;

        var newAmount = (int)Math.Clamp(
            (long)LocalLaborPower + amount,
            0,
            MaxLocalLaborPower);
        var applied = newAmount - LocalLaborPower;
        if (applied <= 0)
            return 0;

        LocalLaborPower = newAmount;
        SendPacket(new SCCharacterLaborPowerChangedPacket(0, applied, 0, 0, 0, 0));
        return applied;
    }

    public void ChangeGamePoints(GamePointKind kind, int change)
    {
        ChangeGamePoints(kind, change, true);
    }

    public void ChangeGamePoints(GamePointKind kind, int change, bool applyGainModifiers)
    {
        switch (kind)
        {
            case GamePointKind.Honor:
                var newHonor = Math.Clamp((long)HonorPoint + change, 0L, int.MaxValue);
                change = (int)(newHonor - HonorPoint);
                HonorPoint = (int)newHonor;
                break;
            case GamePointKind.Vocation:
                if (change > 0 && applyGainModifiers)
                {
                    var vocAdd = GetAttribute(UnitAttribute.LivingPointGain, 0f);
                    change = (int)Math.Clamp(Math.Round(change + vocAdd), 0, int.MaxValue);
                    var vocMul = GetAttribute(UnitAttribute.LivingPointGainMul, 0f) + 100f;
                    change = (int)Math.Clamp(Math.Round(change * (vocMul / 100f)), 0, int.MaxValue);
                }
                var newVocation = Math.Clamp((long)VocationPoint + change, 0L, int.MaxValue);
                change = (int)(newVocation - VocationPoint);
                VocationPoint = (int)newVocation;
                break;
            default:
                Logger.Error($"ChangeGamePoints - Unknown Game Point Type {kind}");
                return;
        }
        SendPacket(new SCGamePointChangedPacket((byte)kind, change));
    }

    public override int GetAbLevel(AbilityType type)
    {
        if (type == AbilityType.General) return Level;
        return ExperienceManager.Instance.GetLevelFromExp(Abilities.Abilities[type].Exp, out _);
    }

    public void ResetSkillCooldown(uint skillId, bool gcd)
    {
        Cooldowns.RemoveCooldown(skillId);
        SendPacket(new SCSkillCooldownResetPacket(this, skillId, 0, gcd));
    }

    public void ResetAllSkillCooldowns(bool triggerGcd)
    {
        const uint playerSkillsTag = 378;
        var skillIds = SkillManager.Instance.GetSkillsByTag(playerSkillsTag);
        foreach (var skillId in skillIds)
        {
            Cooldowns.RemoveCooldown(skillId);
            SendPacket(new SCSkillCooldownResetPacket(this, skillId, 0, triggerGcd));
        }
    }

    public void SetPirate(bool pirate)
    {
        // TODO : If castle owner -> Nope
        var defaultFactionId = CharacterManager.Instance.GetTemplate(Race, Gender).FactionId;

        var newFaction = pirate ? FactionsEnum.Pirate : defaultFactionId;
        var oldFaction = Faction.Id;
        BroadcastPacket(new SCUnitFactionChangedPacket(ObjId, Name, oldFaction, newFaction, false), true);
        Faction = FactionManager.Instance.GetFaction(newFaction);
        if (WorldIntegration.ZoneAuthority)
            WorldIntegration.RelayUnitFactionChangedToZone?.Invoke(ObjId, (int)oldFaction, (int)newFaction, false);
        HousingManager.Instance.UpdateOwnedHousingFaction(Id, newFaction);
        foreach (var doodad in ParentWorld?.SpawnManager?.GetPlayerDoodads(Id) ?? [])
            DoodadManager.Instance.RefreshFaction(doodad, this, doodad.ParentObj as House);
        // TODO : Teleport to Growlgate
        // TODO : Leave guild
    }

    public override void SetPosition(float x, float y, float z, float rotationX, float rotationY, float rotationZ)
    {
        var moved = !Transform.Local.Position.X.Equals(x) || !Transform.Local.Position.Y.Equals(y) || !Transform.Local.Position.Z.Equals(z);
        var lastZoneKey = Transform.ZoneId;
        //Connection.ActiveChar.SendMessage("Move Old Pos: {0}", Transform.ToString());

        base.SetPosition(x, y, z, rotationX, rotationY, rotationZ);

        UpdateUnderWaterState();

        // Connection.ActiveChar.SendMessage("Move New Pos: {0}", Transform.ToString());

        if (!moved)
            return;

        Buffs.TriggerRemoveOn(BuffRemoveOn.Move);

        // Check if zone changed
        if (Transform.ZoneId == lastZoneKey)
            return;
        OnZoneChange(lastZoneKey, Transform.ZoneId);
    }

    /// <summary>
    /// Recomputes <see cref="IsUnderWater"/> from the character's current transform.
    /// </summary>
    /// <remarks>
    /// The client movement path writes straight to <c>Transform.Local</c> and never routes through
    /// <see cref="SetPosition"/>, so this has to be driven from the breath tick as well. Evaluating
    /// it only on <see cref="SetPosition"/> left the flag latched at whatever it was when the
    /// character still sat at the world origin, which is below <c>OceanLevel</c> and therefore
    /// counts as submerged.
    /// </remarks>
    public void UpdateUnderWaterState()
    {
        var world = WorldManager.Instance.GetWorld(Transform.InstanceId);

        // Probe slightly above the character "feet" position to avoid false drowning
        // when standing on a ship deck (server-side Z for attached characters can be lower).
        var probePos = Transform.World.Position;
        Slave attachedSlave = null;

        // Find the closest Slave in the parent chain (direct parent or through sticky parent ancestry).
        for (var t = Transform.Parent; t != null && attachedSlave == null; t = t.Parent)
        {
            if (t.GameObject is Slave s)
                attachedSlave = s;
        }

        for (var t = Transform.StickyParent; t != null && attachedSlave == null; t = t.Parent)
        {
            if (t.GameObject is Slave s)
                attachedSlave = s;
        }

        if (attachedSlave != null)
        {
            var shipModel = ModelManager.Instance.GetShipModel(attachedSlave.ModelId);
            if (shipModel != null)
            {
                // Use a fraction of the ship's vertical bounds as a proxy for deck/head level.
                // If the ship is submerged, this probe will also be submerged.
                var deckProbeOffset = shipModel.MassBoxSizeZ * attachedSlave.Scale * 0.35f;
                var deckProbeZ = attachedSlave.Transform.World.Position.Z + deckProbeOffset;
                if (deckProbeZ > probePos.Z)
                    probePos.Z = deckProbeZ;
            }
        }

        // Breath must match actual water volume (same as physics IsWater): comparing only Z to GetWaterSurface
        // triggers false underwater state when XY projects onto a river polygon but Z is outside the water slab (e.g. bridge).
        if (world == null || !world.IsWater(probePos))
        {
            if (IsUnderWater)
                IsUnderWater = false;
        }
        else
        {
            var waterSurface = world.Water?.GetWaterSurface(probePos, out _) ?? world.Template.OceanLevel;

            const float surfaceBand = 2f;
            const float hysteresis = 0.35f;
            var enterThreshold = waterSurface - surfaceBand;
            var exitThreshold = waterSurface - surfaceBand + hysteresis;

            if (!IsUnderWater && probePos.Z < enterThreshold)
                IsUnderWater = true;
            else if (IsUnderWater && probePos.Z > exitThreshold)
                IsUnderWater = false;
        }
    }

    private CancellationTokenSource _unreleasedZoneTransportedOut;

    public override void OnZoneChange(uint lastZoneKey, uint newZoneKey)
    {
        base.OnZoneChange(lastZoneKey, newZoneKey); // Unit

        var lastZone = ZoneManager.Instance.GetZoneByKey(lastZoneKey);
        var lastZoneGroupId = (short)(lastZone?.GroupId ?? 0);
        var newZone = ZoneManager.Instance.GetZoneByKey(newZoneKey);
        var newZoneGroupId = (short)(newZone?.GroupId ?? 0);

        // Ok, we actually changed zone groups, we'll have to do some chat channel stuff
        if (lastZoneGroupId != 0)
            ChatManager.Instance.GetZoneChat(lastZoneKey).LeaveChannel(this);
        if (newZoneGroupId != 0)
            ChatManager.Instance.GetZoneChat(newZoneKey).JoinChannel(this);

        // ZoneAuthority: sim presence follows zone key (WZUnitRemoved old + WZUnitState new).
        if (WorldIntegration.ZoneAuthority && lastZoneKey != 0 && newZoneKey != 0 && lastZoneKey != newZoneKey)
        {
            var body = WorldIntegration.BuildWzUnitStateBody(this);
            var accepted = WorldIntegration.RelayCharacterZoneHandoff?.Invoke(
                ObjId, lastZoneKey, newZoneKey, body) ?? false;
            if (!accepted)
            {
                Logger.Error(
                    "Zone handoff refused for {0} (ObjId={1}) from zone {2} to {3}; returning to character select",
                    Name, ObjId, lastZoneKey, newZoneKey);

                // Leave the Transform.ZoneId callback before world cleanup detaches the character hierarchy.
                var failedConnection = Connection;
                if (failedConnection != null)
                {
                    _ = Task.Run(() => EnterWorldManager.Instance.ReturnToCharacterSelect(
                        failedConnection,
                        $"zone {newZoneKey} is not available"));
                }

                return;
            }
        }

        if (newZone != null)
        {
            Expedition?.OnCharacterRefresh(this);
        }

        if (newZone is { Closed: false })
        {
            if (_unreleasedZoneTransportedOut != null)
            {
                _unreleasedZoneTransportedOut.Cancel();
                _unreleasedZoneTransportedOut = null;
            }

            return;
        }

        if (newZone != null)
            SendMessage(ChatType.System, $"You have entered a closed zone ({newZone.ZoneKey} - {newZone.Name})!\nPlease leave immediately!", Color.Red);

        var characterAccessLevel = CharacterManager.Instance.GetEffectiveAccessLevel(this);
        if (characterAccessLevel < 100)
        {
            // Do forbidden zone code handling
            if (_unreleasedZoneTransportedOut != null)
            {
                return;
            }

            _unreleasedZoneTransportedOut = new CancellationTokenSource();
            Task.Run(async () =>
            {
                // Stay for a maximum of 10 seconds
                for (var i = 0; i < 5; i++)
                {
                    // sendErrorMsg
                    SendErrorMessage(ErrorMessageType.ClosedZone, 0, false);
                    await Task.Delay(2 * 1000, _unreleasedZoneTransportedOut.Token);
                }
                ForceDismount();
                ParentWorld.MateManager.RemoveAndDespawnAllActiveOwnedMates(this);
                await Task.Delay(200);
                var portal = PortalManager.Instance.GetClosestReturnPortal(Connection.ActiveChar);
                // force transported out
                Connection.ActiveChar.BroadcastPacket(
                    new SCCharacterResurrectedPacket(
                        Connection.ActiveChar.ObjId,
                        portal.X,
                        portal.Y,
                        portal.Z,
                        portal.ZRot
                    ),
                    true
                );

            }, _unreleasedZoneTransportedOut.Token);
        }
    }

    public override int DoFallDamage(float impactSpeed)
    {
        if (CharacterManager.Instance.GetEffectiveAccessLevel(this) >= AppConfiguration.Instance.World.IgnoreFallDamageAccessLevel)
        {
            Logger.Debug($"{Name} negated FallDamage because of IgnoreFallDamageAccessLevel settings");
            return 0; // GM & Admin take 0 damage from falling
        }
        var fallDamage = base.DoFallDamage(impactSpeed);
        Logger.Trace($"FallDamage: {Name} - impactSpeed {impactSpeed:F2} m/s, Damage {fallDamage}");
        return fallDamage;
    }

    /// <summary>
    /// ItemUse - is used to work the quests
    /// </summary>
    /// <param name="id">item.id</param>
    public void ItemUse(ulong id)
    {
        var item = Inventory.GetItemById(id);
        if (item is { Count: > 0 })
        {
            // Trigger event
            Events?.OnItemUse(this, new OnItemUseArgs
            {
                ItemId = item.TemplateId
            });
        }
    }

    /// <summary>
    /// ItemUse - is used to work the quests
    /// </summary>
    /// <param name="item"></param>
    public void ItemUse(Item item)
    {
        if (item is not null)
        {
            // Trigger event
            Events?.OnItemUse(this, new OnItemUseArgs
            {
                ItemId = item.TemplateId
            });
        }
    }

    /// <summary>
    /// Trigger OnItemUse using a item template
    /// </summary>
    /// <param name="itemTemplate"></param>
    public void ItemUseByTemplate(uint itemTemplate)
    {
        if (itemTemplate > 0)
        {
            // Trigger event
            Events?.OnItemUse(this, new OnItemUseArgs
            {
                ItemId = itemTemplate
            });
        }
    }

    public void SetAction(byte slot, ActionSlotType type, uint actionId)
    {
        Slots[slot].Type = type;
        Slots[slot].ActionId = actionId;
    }

    public void SetAction(byte slot, ActionSlotType type, ulong itemId)
    {
        Slots[slot].Type = type;
        Slots[slot].ActionId = itemId;
    }

    public void SetOption(ushort key, string value)
    {
        _options[key] = value;
    }

    public string GetOption(ushort key)
    {
        if (_options.TryGetValue(key, out var option))
            return option;
        return "";
    }

    public void PushSubscriber(IDisposable disposable)
    {
        Subscribers.Add(disposable);
    }

    public void SendOption(ushort key)
    {
        Connection.SendPacket(new SCResponseUIDataPacket(Id, key, GetOption(key)));
    }

    /// <summary>
    /// Sends a chat message
    /// </summary>
    /// <param name="type">Chat Type to use</param>
    /// <param name="message">The actual text</param>
    /// <param name="color">If set, adds a color tags to the beginning and the end of the text</param>
    public void SendMessage(ChatType type, string message, Color? color = null)
    {
        if (color != null)
            message = $"|c{color.Value.A:X2}{color.Value.R:X2}{color.Value.G:X2}{color.Value.B:X2}{message}|r";
        SendPacket(new SCChatMessagePacket(type, message));
    }

    public void SendMessage(string message) => SendMessage(ChatType.System, message);

    /// <summary>
    /// Sends a debug message to player chat, but only if DebugInfo is enabled in the configuration
    /// </summary>
    /// <param name="message"></param>
    public void SendDebugMessage(string message)
    {
        if (AppConfiguration.Instance.DebugInfo && CharacterManager.Instance.GetEffectiveAccessLevel(this) >= AppConfiguration.Instance.DebugInfoLevel)
            SendMessage(ChatType.System, message);
    }
    
    /// <summary>
    /// Sends an error message to the player
    /// </summary>
    /// <param name="errorMsgType">Error Id</param>
    /// <param name="type">Addition argument for error if needed</param>
    /// <param name="isNotify">If true, will also give a popup-text</param>
    public void SendErrorMessage(ErrorMessageType errorMsgType, uint type = 0, bool isNotify = true)
    {
        SendPacket(new SCErrorMsgPacket(errorMsgType, type, isNotify));
    }

    /// <summary>
    /// Sends an error message to the player that also has a subtype
    /// </summary>
    /// <param name="errorMsgType1"></param>
    /// <param name="errorMsgType2"></param>
    /// <param name="type"></param>
    /// <param name="isNotify"></param>
    public void SendErrorMessage(ErrorMessageType errorMsgType1, ErrorMessageType errorMsgType2, uint type = 0, bool isNotify = true)
    {
        SendPacket(new SCErrorMsgPacket(errorMsgType1, errorMsgType2, type, isNotify));
    }

    public static Character Load(uint characterId, uint accountId)
    {
        using (var connection = MySQL.CreateConnection())
            return Load(connection, characterId, accountId);
    }

    public static Character Load(uint characterId)
    {
        using (var connection = MySQL.CreateConnection())
            return Load(connection, characterId);
    }

    public uint Breath { get; set; }

    public bool IsDrowning
    {
        get { return Breath <= 0; }
    }

    public TimeSpan OnlineTime { get; set; } = TimeSpan.Zero;

    public override void ReduceCurrentHp(BaseUnit attacker, int value, KillReason killReason = KillReason.Damage)
    {
        if (AppConfiguration.Instance.World.GodMode)
        {
            Logger.Debug($"{Name}'s damage disabled because of GodMode flag (normal damage: {value})");
            return; // GodMode On : take no damage at all
        }

        if (IsInDuel)
        {
            Hp = Math.Max(Hp - value, 1); // we don't let you die during a duel
            value = 0;
        }

        // PvP assist tracking: remember who hit us recently
        if (attacker is Character enemyChar && value > 0 && enemyChar.Id != this.Id)
            RecordPvpDamageFrom(enemyChar);

        base.ReduceCurrentHp(attacker, value, killReason);
    }

    public void DoRepair(List<Item> items, bool useAaPoint)
    {
        var tasks = new List<ItemTask>();
        var repairs = new List<(EquipItem EquipItem, Item Item)>();
        long repairCost = 0;

        foreach (var item in items)
        {
            if (item == null)
                continue;

            if (!Inventory.Bag.Items.Contains(item) && !Equipment.Items.Contains(item))
            {
                Logger.Warn($"Attempting to repair an item that isn't in your inventory or equipment, Item: {item.Id}");
                continue;
            }

            if (!(item is EquipItem equipItem && item.Template is EquipItemTemplate))
            {
                Logger.Warn($"Attempting to repair a non-equipment item, Item: {item.Id}");
                continue;
            }

            if (equipItem.Durability >= equipItem.MaxDurability)
            {
                Logger.Warn($"Attempting to repair an item that has max durability, Item: {item.Id}");
                continue;
            }

#pragma warning disable CA1508 // Avoid dead conditional code
            if (CurrentInteractionObject is null || CurrentInteractionObject is not Npc npc)
                continue;
#pragma warning restore CA1508 // Avoid dead conditional code

            if (!npc.Template.Blacksmith)
            {
                Logger.Warn($"Attempting to repair an item while not at a blacksmith, Item: {item.Id}, NPC: {npc}");
                continue;
            }

            var dist = MathUtil.CalculateDistance(Transform.World.Position, npc.Transform.World.Position);

            if (dist > 5f)
            {
                SendErrorMessage(ErrorMessageType.TooFarAway);
                continue;
            }

            var currentRepairCost = equipItem.RepairCost;

            var repairBalance = useAaPoint ? AaPoint : Money;
            if (currentRepairCost < 0 || repairCost > long.MaxValue - currentRepairCost ||
                repairCost + currentRepairCost > repairBalance)
            {
                Logger.Warn(
                    $"Not enough {(useAaPoint ? "AA points" : "money")} to repair, Item: {item.Id}, " +
                    $"Balance: {repairBalance}, " +
                    $"SelectedRepairCost: {repairCost + currentRepairCost}");
                continue;
            }

            repairCost += currentRepairCost;
            repairs.Add((equipItem, item));
        }

        if (repairs.Count == 0)
            return;

        if (repairCost > 0)
        {
            var paid = useAaPoint
                ? SubtractAAPoint(SlotType.Inventory, repairCost, ItemTaskType.Repair)
                : SubtractMoney(SlotType.Inventory, repairCost, ItemTaskType.Repair);
            if (!paid)
                return;
        }

        foreach (var (equipItem, item) in repairs)
        {
            equipItem.Durability = equipItem.MaxDurability;
            equipItem.IsDirty = true;
            tasks.Add(new ItemUpdate(item));
        }

        Connection.SendPacket(new SCItemTaskSuccessPacket(ItemTaskType.Repair, tasks, []));
    }

    /// <summary>
    /// Forcibly remove character from any mount or vehicle they might be riding,
    /// useful for calling before any kind of teleport function 
    /// </summary>
    /// <returns>Returns True is any dismounting happened by this function</returns>
    public bool ForceDismount(AttachUnitReason reason = AttachUnitReason.PrefabChanged)
    {
        var res = false;
        // Force dismount Mates (mounts)
        var isOnMount = ParentWorld.MateManager.GetIsMounted(ObjId, out var attachedRiderPoint);
        if (isOnMount != null)
        {
            ParentWorld.MateManager.UnMountMate(this, isOnMount.TlId, attachedRiderPoint, reason);
            res = true;
        }
        // Force remove from slaves
        var isOnSlave = ParentWorld.SlaveManager.GetIsMounted(ObjId, out _);
        if (isOnSlave != null)
        {
            ParentWorld.SlaveManager.UnbindSlave(this, isOnSlave.TlId, reason);
            res = true;
        }
        // Mast/ladder hang: DetachAll alone leaves the client in Hung state — must SCUnhung to self.
        // Also re-notify after sail BindSlave: client hang often survives CSUnhang (was self=false).
        var hangTargetObjId = Transform.StickyParent?.GameObject?.ObjId ?? 0;
        var stickySlave = Transform.StickyParent?.GameObject as Slave;
        var wasHanging = Transform.StickyParent != null;
        if (wasHanging)
        {
            Transform.StickyParent = null;
            if (stickySlave != null)
                ShipHarpoonRopeController.BreakRopeForClients(stickySlave, cutouted: false);
            res = true;
        }

        if (wasHanging || isOnSlave != null)
            BroadcastPacket(new SCUnhungPacket(ObjId, hangTargetObjId, 0), true);
        // Unbind from any parent
        Transform.DetachAll();
        return res;
    }

    public bool ForceDismountAndDespawn(AttachUnitReason reason = AttachUnitReason.PrefabChanged, int timeToDespawn = 1000 * 60 * 10)
    {
        var res = ForceDismount();

        var mySlave = ParentWorld.SlaveManager.GetActiveSlaveByOwnerObjId(Connection.ActiveChar.ObjId);
        if (mySlave != null)
        {
            // run the task to turn off the transport after timeToDespawn minutes
            mySlave.CancelTokenSource = new CancellationTokenSource();
            var token = mySlave.CancelTokenSource.Token;
            mySlave.LeaveTask = new Task(() =>
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(timeToDespawn)); // 10 minutes
                if (token.IsCancellationRequested)
                    return;
                ParentWorld.SlaveManager.RemoveAndDespawnAllActiveOwnedSlaves(this);
            }, token);
            mySlave.LeaveTask.Start();
        }

        return res;
    }

    /// <summary>
    /// ForceDismountAndDespawn - deleting Mirage's test transport
    /// </summary>
    /// <param name="slave"></param>
    /// <param name="timeToDespawn"></param>
    /// <returns></returns>
    public bool ForceDismountAndDespawn(Slave slave, int timeToDespawn = 100)
    {
        var res = ForceDismount();

        if (slave != null)
        {
            // run the task to turn off the transport after timeToDespawn minutes
            slave.CancelTokenSource = new CancellationTokenSource();
            var token = slave.CancelTokenSource.Token;
            slave.LeaveTask = new Task(() =>
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(timeToDespawn));
                if (token.IsCancellationRequested)
                    return;
                ParentWorld.SlaveManager.RemoveAndDespawnTestSlave(this, slave.ObjId);
            }, token);
            slave.LeaveTask.Start();
        }

        return res;
    }

    public void RemoveAndDespawnActiveOwnedMatesSlaves()
    {
        // Despawn and unmount everybody from owned Mates
        ParentWorld.MateManager.RemoveAndDespawnAllActiveOwnedMates(this);
        ForceDismountAndDespawn();
    }

    #region Database

    public static Character Load(MySqlConnection connection, uint characterId, uint accountId)
    {
        var accountDetails = AccountManager.Instance.GetAccountDetails(accountId);
        Character character = null;
        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.CommandText = "SELECT * FROM characters WHERE `id` = @id AND `account_id` = @account_id and `deleted`=0";
            command.Parameters.AddWithValue("@id", characterId);
            command.Parameters.AddWithValue("@account_id", accountId);
            using (var reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    var stream = (PacketStream)(byte[])reader.GetValue("unit_model_params");
                    var modelParams = new UnitCustomModelParams();
                    modelParams.Read(stream);

                    character = new Character(modelParams)
                    {
                        AccountId = accountId, Id = reader.GetUInt32("id"), Name = reader.GetString("name"), AccessLevel = reader.GetInt32("access_level"),
                        Race = (Race)reader.GetByte("race"),
                        Gender = (Gender)reader.GetByte("gender"),
                        Level = reader.GetByte("level"),
                        Experience = reader.GetInt32("experience"),
                        RecoverableExp = reader.GetInt32("recoverable_exp"),
                        HeirExp = reader.GetInt64("heir_exp"),
                        Hp = reader.GetInt32("hp"),
                        Mp = reader.GetInt32("mp")
                    };
                    character._savedHp = character.Hp; // save for later
                    character._savedMp = character.Mp;
                    // character.LaborPower = reader.GetInt16("labor_power");
                    // character.LaborPowerModified = reader.GetDateTime("labor_power_modified");
                    character.InitializeLaborCache(accountDetails.Labor, accountDetails.LastUpdated);
                    character.ConsumedLaborPower = reader.GetInt32("consumed_lp");
                    character.LocalLaborPower = reader.GetInt32("local_lp");
                    character.Ability1 = (AbilityType)reader.GetByte("ability1");
                    character.Ability2 = (AbilityType)reader.GetByte("ability2");
                    character.Ability3 = (AbilityType)reader.GetByte("ability3");
                    character.ServerId = reader.GetUInt32("world_id");
                    character.Transform = new Transform(character, null, 
                        reader.GetUInt32("zone_id"), WorldManager.DefaultInstanceId,
                        reader.GetFloat("x"), reader.GetFloat("y"), reader.GetFloat("z"),
                        reader.GetFloat("yaw"), reader.GetFloat("pitch"), reader.GetFloat("roll")
                        );
                    character.Faction = FactionManager.Instance.GetFaction((FactionsEnum)reader.GetUInt32("faction_id"));
                    character.FactionName = reader.GetString("faction_name");
                    character.Expedition = ExpeditionManager.Instance.GetExpedition((FactionsEnum)reader.GetUInt32("expedition_id"));
                    character.Family = reader.GetUInt32("family");
                    character.DeadCount = reader.GetInt16("dead_count");
                    character.DeadTime = reader.GetDateTime("dead_time");
                    character.RezWaitDuration = reader.GetInt32("rez_wait_duration");
                    character.RezTime = reader.GetDateTime("rez_time");
                    character.RezPenaltyDuration = reader.GetInt32("rez_penalty_duration");
                    character.LeaveTime = reader.GetDateTime("leave_time");
                    character.Money = reader.GetInt64("money");
                    character.Money2 = reader.GetInt64("money2");
                    character.AaPoint = reader.GetInt64("aa_point");
                    character.BankAaPoint = reader.GetInt64("bank_aa_point");
                    character.HonorPoint = reader.GetInt32("honor_point");
                    character.VocationPoint = reader.GetInt32("vocation_point");
                    character.CrimePoint = reader.GetInt16("crime_point");
                    character.TotalPlayTime = reader.GetUInt32("total_play_time");
                    character.CrimeRecord = reader.GetInt32("crime_record");
                    character.JuryPoint = reader.GetInt32("jury_point");
                    character.HostileFactionKills = reader.GetUInt32("hostile_faction_kills");
                    character.HonorGainedInCombat = reader.GetUInt32("pvp_honor");
                    character.DiedInPvp = reader.GetBoolean("died_in_pvp");
                    character.DiedInPvpWarZone = reader.GetBoolean("died_in_pvp_war_zone");
                    character.TransferRequestTime = reader.GetDateTime("transfer_request_time");
                    character.DeleteRequestTime = reader.GetDateTime("delete_request_time");
                    character.DeleteTime = reader.GetDateTime("delete_time");
                    character.AutoUseAAPoint = reader.GetBoolean("auto_use_aapoint");
                    character.PrivacyStatus = (CharacterPrivacyStatus)reader.GetSByte("privacy_status");
                    character.PrevPoint = reader.GetInt32("prev_point");
                    character.Point = reader.GetInt32("point");
                    character.Gift = reader.GetInt32("gift");
                    character.NumInventorySlots = reader.GetByte("num_inv_slot");
                    character.NumBankSlots = reader.GetInt16("num_bank_slot");
                    character.ExpandedExpert = reader.GetByte("expanded_expert");
                    character.Created = reader.GetDateTime("created_at");
                    character.Updated = reader.GetDateTime("updated_at");
                    character.ReturnDistrictId = reader.GetUInt32("return_district");
                    character.OnlineTime = TimeSpan.FromSeconds(reader.GetUInt32("online_time"));

                    character.Inventory = new Inventory(character);

                    var slotsBlob = (PacketStream)(byte[])reader.GetValue("slots");
                    character.LoadActionSlots(slotsBlob);

                    character.BmPoint = AccountManager.Instance.GetAccountDetails(character.AccountId).Loyalty;

                    if (character.Hp > character.MaxHp)
                        character.Hp = character.MaxHp;
                    if (character.Mp > character.MaxMp)
                        character.Mp = character.MaxMp;
                    character.ValidateAndFixExpAndLevel();
                    character.PostUpdateCurrentHp(character, 0, character.Hp, KillReason.Unknown);
                }
            }
        }

        if (character == null)
            return null;

        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.CommandText = "SELECT * FROM `options` WHERE `owner` = @owner";
            command.Parameters.AddWithValue("@owner", characterId);
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    var key = reader.GetUInt16("key");
                    var value = reader.GetString("value");
                    character.SetOption(key, value);
                }
            }
        }

        return character;
    }

    public static Character Load(MySqlConnection connection, uint characterId)
    {
        Character character = null;
        using (var command = connection.CreateCommand())
        {
            command.Connection = connection;
            command.CommandText = "SELECT * FROM characters WHERE `id` = @id and `deleted`=0";
            command.Parameters.AddWithValue("@id", characterId);
            using (var reader = command.ExecuteReader())
            {
                if (reader.Read())
                {
                    var stream = (PacketStream)(byte[])reader.GetValue("unit_model_params");
                    var modelParams = new UnitCustomModelParams();
                    modelParams.Read(stream);

                    character = new Character(modelParams) { Id = reader.GetUInt32("id"), AccountId = reader.GetUInt32("account_id") };

                    var accountDetails = AccountManager.Instance.GetAccountDetails(character.AccountId);

                    character.Name = reader.GetString("name");
                    character.AccessLevel = reader.GetInt32("access_level");
                    character.Race = (Race)reader.GetByte("race");
                    character.Gender = (Gender)reader.GetByte("gender");
                    character.Level = reader.GetByte("level");
                    character.Experience = reader.GetInt32("experience");
                    character.RecoverableExp = reader.GetInt32("recoverable_exp");
                    character.HeirExp = reader.GetInt64("heir_exp");
                    character.Hp = reader.GetInt32("hp");
                    character.Mp = reader.GetInt32("mp");
                    character._savedHp = character.Hp; // save for later
                    character._savedMp = character.Mp;
                    character.InitializeLaborCache(accountDetails.Labor, accountDetails.LastUpdated);
                    // character.LaborPower = reader.GetInt16("labor_power");
                    // character.LaborPowerModified = reader.GetDateTime("labor_power_modified");
                    character.ConsumedLaborPower = reader.GetInt32("consumed_lp");
                    character.LocalLaborPower = reader.GetInt32("local_lp");
                    character.Ability1 = (AbilityType)reader.GetByte("ability1");
                    character.Ability2 = (AbilityType)reader.GetByte("ability2");
                    character.Ability3 = (AbilityType)reader.GetByte("ability3");
                    character.ServerId = reader.GetUInt32("world_id");
                    character.Transform = new Transform(character, null, 
                        reader.GetUInt32("zone_id"), WorldManager.DefaultInstanceId,
                        reader.GetFloat("x"), reader.GetFloat("y"), reader.GetFloat("z"),
                        reader.GetFloat("yaw"), reader.GetFloat("pitch"), reader.GetFloat("roll")
                        );
                    character.Faction = FactionManager.Instance.GetFaction((FactionsEnum)reader.GetUInt32("faction_id"));
                    character.FactionName = reader.GetString("faction_name");
                    character.Expedition = ExpeditionManager.Instance.GetExpedition((FactionsEnum)reader.GetUInt32("expedition_id"));
                    character.Family = reader.GetUInt32("family");
                    character.DeadCount = reader.GetInt16("dead_count");
                    character.DeadTime = reader.GetDateTime("dead_time");
                    character.RezWaitDuration = reader.GetInt32("rez_wait_duration");
                    character.RezTime = reader.GetDateTime("rez_time");
                    character.RezPenaltyDuration = reader.GetInt32("rez_penalty_duration");
                    character.LeaveTime = reader.GetDateTime("leave_time");
                    character.Money = reader.GetInt64("money");
                    character.Money2 = reader.GetInt64("money2");
                    character.AaPoint = reader.GetInt64("aa_point");
                    character.BankAaPoint = reader.GetInt64("bank_aa_point");
                    character.HonorPoint = reader.GetInt32("honor_point");
                    character.VocationPoint = reader.GetInt32("vocation_point");
                    character.CrimePoint = reader.GetInt16("crime_point");
                    character.TotalPlayTime = reader.GetUInt32("total_play_time");
                    character.CrimeRecord = reader.GetInt32("crime_record");
                    character.JuryPoint = reader.GetInt16("jury_point");
                    character.HostileFactionKills = reader.GetUInt32("hostile_faction_kills");
                    character.HonorGainedInCombat = reader.GetUInt32("pvp_honor");
                    character.DiedInPvp = reader.GetBoolean("died_in_pvp");
                    character.DiedInPvpWarZone = reader.GetBoolean("died_in_pvp_war_zone");
                    character.TransferRequestTime = reader.GetDateTime("transfer_request_time");
                    character.DeleteRequestTime = reader.GetDateTime("delete_request_time");
                    character.DeleteTime = reader.GetDateTime("delete_time");
                    // character.BmPoint = reader.GetInt32("bm_point");
                    character.AutoUseAAPoint = reader.GetBoolean("auto_use_aapoint");
                    character.PrivacyStatus = (CharacterPrivacyStatus)reader.GetSByte("privacy_status");
                    character.PrevPoint = reader.GetInt32("prev_point");
                    character.Point = reader.GetInt32("point");
                    character.Gift = reader.GetInt32("gift");
                    character.NumInventorySlots = reader.GetByte("num_inv_slot");
                    character.NumBankSlots = reader.GetInt16("num_bank_slot");
                    character.ExpandedExpert = reader.GetByte("expanded_expert");
                    character.Created = reader.GetDateTime("created_at");
                    character.Updated = reader.GetDateTime("updated_at");
                    character.ReturnDistrictId = reader.GetUInt32("return_district");
                    character.OnlineTime = TimeSpan.FromSeconds(reader.GetUInt32("online_time"));

                    character.Inventory = new Inventory(character);

                    var slotsBlob = (PacketStream)(byte[])reader.GetValue("slots");
                    character.LoadActionSlots(slotsBlob);

                    character.BmPoint = AccountManager.Instance.GetAccountDetails(character.AccountId).Loyalty;

                    if (character.Hp > character.MaxHp)
                        character.Hp = character.MaxHp;
                    if (character.Mp > character.MaxMp)
                        character.Mp = character.MaxMp;
                    character.ValidateAndFixExpAndLevel();
                    character.PostUpdateCurrentHp(character, 0, character.Hp, KillReason.Unknown);
                }
            }
        }

        if (character == null)
            return null;

        return character;
    }

    private void LoadActionSlots(PacketStream slotsBlob)
    {
        if (Slots == null)
        {
            Slots = new ActionSlot[MaxActionSlots];
            for (var i = 0; i < Slots.Length; i++)
                Slots[i] = new ActionSlot();
        }

        foreach (var slot in Slots)
        {
            slot.Type = (ActionSlotType)slotsBlob.ReadByte();
            switch (slot.Type)
            {
                case ActionSlotType.None:
                    {
                        break;
                    }
                case ActionSlotType.ItemType:
                case ActionSlotType.Spell:
                case ActionSlotType.RidePetSpell:
                    {
                        slot.ActionId = slotsBlob.ReadUInt32();
                        break;
                    }
                case ActionSlotType.ItemId:
                    {
                        slot.ActionId = slotsBlob.ReadUInt64(); // itemId
                        break;
                    }
                default:
                    {
                        Logger.Error("LoadActionSlots, Unknown ActionSlotType!");
                        break;
                    }
            }
        }
    }

    private void LoadActionSlots(MySqlConnection connection)
    {
        try
        {
            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.CommandText = "SELECT slots FROM `characters` WHERE `id` = @id AND `account_id` = @account_id";
                command.Parameters.AddWithValue("@id", Id);
                command.Parameters.AddWithValue("@account_id", AccountId);
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        var slotsBlob = (PacketStream)(byte[])reader.GetValue("slots");
                        LoadActionSlots(slotsBlob);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"LoadActionSlots, error while loading for character {Id}, {ex.Message}");
        }
    }

    private byte[] GetActionSlotsAsBlob()
    {
        var slotsBlob = new PacketStream();
        foreach (var slot in Slots)
        {
            slotsBlob.Write((byte)slot.Type);

            switch (slot.Type)
            {
                case ActionSlotType.None:
                    {
                        break;
                    }
                case ActionSlotType.ItemType:
                case ActionSlotType.Spell:
                case ActionSlotType.RidePetSpell:
                    {
                        slotsBlob.Write((uint)slot.ActionId);
                        break;
                    }
                case ActionSlotType.ItemId:
                    {
                        slotsBlob.Write(slot.ActionId); // itemId
                        break;
                    }
                default:
                    {
                        Logger.Error("GetActionSlotsAsBlob, Unknown ActionSlotType!");
                        break;
                    }
            }
        }
        return slotsBlob.GetBytes();
    }

    public void Load()
    {
        var template = CharacterManager.Instance.GetTemplate(Race, Gender);
        ModelId = template.ModelId;
        BuyBackItems = new ItemContainer(Id, SlotType.None, false, this);
        Slots = new ActionSlot[MaxActionSlots];
        for (var i = 0; i < Slots.Length; i++)
            Slots[i] = new ActionSlot();

        Craft = new CharacterCraft(this);
        Procs = new UnitProcs(this);
        LocalPingPosition = new WorldSpawnPosition();

        using (var connection = MySQL.CreateConnection())
        {
            // Inventory.Load(connection);
            Abilities = new CharacterAbilities(this);
            Abilities.Load(connection);
            Actability = new CharacterActability(this);
            Actability.Load(connection);
            Skills = new CharacterSkills(this);
            Skills.Load(connection);
            SkillActiveTypes = new CharacterSkillActiveTypes(this);
            SkillActiveTypes.Load(connection);
            HeirSkills = new CharacterHeirSkills(this);
            HeirSkills.Load(connection);
            Appellations = new CharacterAppellations(this);
            Appellations.Load(connection);
            Portals = new CharacterPortals(this);
            Portals.Load(connection);
            Friends = new CharacterFriends(this);
            Friends.Load(connection);
            Blocked = new CharacterBlocked(this);
            Blocked.Load(connection);
            FavoriteCrafts = new CharacterFavoriteCrafts(this);
            FavoriteCrafts.Load(connection);
            Quests = new CharacterQuests(this);
            Quests.Load(connection);
            Quests.CheckDailyResetAtLogin();
            Mates = new CharacterMates(this);
            Mates.Load(connection);

            LoadActionSlots(connection);
        }

        Mails = new CharacterMails(this);
        // Counts only — the character is not in the world list yet, and the delivery notifications
        // GetCurrentMailList sends belong to an in-world client, not to character load.
        Mails.RefreshUnreadCount();
        // Update sync housing factions on login
        HousingManager.Instance.UpdateOwnedHousingFaction(Id, Faction.Id);
    }

    public bool SaveDirectlyToDatabase()
    {
        // Try to save New Character
        bool saved;
        using (var sqlConnection = MySQL.CreateConnection())
        {
            using (var transaction = sqlConnection.BeginTransaction())
            {
                try
                {
                    saved = Save(sqlConnection, transaction);
                    if (!saved)
                    {
                        transaction.Rollback();
                        return false;
                    }

                    // Persist the character's items in the same transaction. The lobby/character list reloads
                    // everything from the DB (GameConnection.LoadAccount), so freshly-created gear — including the
                    // face/hair/body appearance parts — must be written now, not left for the periodic SaveManager.
                    ItemManager.Instance.Save(sqlConnection, transaction);
                    transaction.Commit();
                }
                catch (Exception e)
                {
                    saved = false;
                    Logger.Error(e, $"Character save failed for {Id} - {Name}");
                    try
                    {
                        transaction.Rollback();
                    }
                    catch (Exception eRollback)
                    {
                        // Really failed here
                        Logger.Fatal(eRollback, $"Character save rollback failed for {Id} - {Name}");
                    }
                }
            }
        }
        return saved;
    }

    public bool Save(MySqlConnection connection, MySqlTransaction transaction)
    {
        bool result;
        try
        {
            var unitModelParams = ModelParams.Write(new PacketStream()).GetBytes();

            Updated = DateTime.UtcNow; // обновим время записи информации

            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                // ----
                command.CommandText =
                    "REPLACE INTO `characters` " +
                    "(`id`,`account_id`,`name`,`access_level`,`race`,`gender`,`unit_model_params`,`level`,`experience`,`recoverable_exp`,`heir_exp`," +
                    "`hp`,`mp`,`consumed_lp`,`local_lp`,`ability1`,`ability2`,`ability3`," +
                    "`world_id`,`zone_id`,`x`,`y`,`z`,`roll`,`pitch`,`yaw`," +
                    "`faction_id`,`faction_name`,`expedition_id`,`family`,`dead_count`,`dead_time`,`rez_wait_duration`,`rez_time`,`rez_penalty_duration`,`leave_time`," +
                    "`money`,`money2`,`aa_point`,`bank_aa_point`,`honor_point`,`vocation_point`,`crime_point`,`crime_record`,`jury_point`," +
                    "`hostile_faction_kills`,`pvp_honor`,`died_in_pvp`,`died_in_pvp_war_zone`," +
                    "`delete_request_time`,`transfer_request_time`,`delete_time`,`auto_use_aapoint`,`prev_point`,`point`,`gift`," +
                    "`num_inv_slot`,`num_bank_slot`,`expanded_expert`,`slots`,`created_at`,`updated_at`,`return_district`,`online_time`,`total_play_time`,`privacy_status`" +
                    ") VALUES (" +
                    "@id,@account_id,@name,@access_level,@race,@gender,@unit_model_params,@level,@experience,@recoverable_exp,@heir_exp," +
                    "@hp,@mp,@consumed_lp,@local_lp,@ability1,@ability2,@ability3," +
                    "@world_id,@zone_id,@x,@y,@z,@yaw,@pitch,@roll," +
                    "@faction_id,@faction_name,@expedition_id,@family,@dead_count,@dead_time,@rez_wait_duration,@rez_time,@rez_penalty_duration,@leave_time," +
                    "@money,@money2,@aa_point,@bank_aa_point,@honor_point,@vocation_point,@crime_point,@crime_record,@jury_point," +
                    "@hostile_faction_kills,@pvp_honor,@died_in_pvp,@died_in_pvp_war_zone," +
                    "@delete_request_time,@transfer_request_time,@delete_time,@auto_use_aapoint,@prev_point,@point,@gift," +
                    "@num_inv_slot,@num_bank_slot,@expanded_expert,@slots,@created_at,@updated_at,@return_district,@online_time,@total_play_time,@privacy_status)";

                command.Parameters.AddWithValue("@id", Id);
                command.Parameters.AddWithValue("@account_id", AccountId);
                command.Parameters.AddWithValue("@name", Name);
                command.Parameters.AddWithValue("@access_level", AccessLevel);
                command.Parameters.AddWithValue("@race", (byte)Race);
                command.Parameters.AddWithValue("@gender", (byte)Gender);
                command.Parameters.AddWithValue("@unit_model_params", unitModelParams);
                command.Parameters.AddWithValue("@level", Level);
                command.Parameters.AddWithValue("@experience", Experience);
                command.Parameters.AddWithValue("@recoverable_exp", RecoverableExp);
                command.Parameters.AddWithValue("@heir_exp", HeirExp);
                command.Parameters.AddWithValue("@hp", Hp);
                command.Parameters.AddWithValue("@mp", Mp);
                command.Parameters.AddWithValue("@consumed_lp", ConsumedLaborPower);
                command.Parameters.AddWithValue("@local_lp", LocalLaborPower);
                command.Parameters.AddWithValue("@ability1", (byte)Ability1);
                command.Parameters.AddWithValue("@ability2", (byte)Ability2);
                command.Parameters.AddWithValue("@ability3", (byte)Ability3);
                command.Parameters.AddWithValue("@world_id", ServerId);
                // Position saving rule (portal / small world fix):
                // MainWorldPosition is set when a player enters a system instance (dungeon /
                // small world) and is used as the return point. The stock code preferred
                // MainWorldPosition whenever it was non-null. The problem: MainWorldPosition
                // stays non-null AFTER leaving an instance (it is reused by Return/portal
                // logic), so once a player had visited an instance, every later save wrote the
                // stale instance-return position instead of the player's real current position
                // -- which teleported the player back to the exit portal on reconnect.
                //
                // Fix: only fall back to MainWorldPosition when the player is *currently* inside
                // a non-default instance. When back in the main world, always save the live
                // Transform.
                var saveFromInstanceReturn =
                    MainWorldPosition != null &&
                    Transform.InstanceId != WorldManager.DefaultInstanceId;
                command.Parameters.AddWithValue("@zone_id", saveFromInstanceReturn ? MainWorldPosition.ZoneId : Transform.ZoneId);
                command.Parameters.AddWithValue("@x", saveFromInstanceReturn ? MainWorldPosition.World.Position.X : Transform.World.Position.X);
                command.Parameters.AddWithValue("@y", saveFromInstanceReturn ? MainWorldPosition.World.Position.Y : Transform.World.Position.Y);
                command.Parameters.AddWithValue("@z", saveFromInstanceReturn ? MainWorldPosition.World.Position.Z : Transform.World.Position.Z);
                command.Parameters.AddWithValue("@roll", saveFromInstanceReturn ? MainWorldPosition.World.Rotation.X : Transform.World.Rotation.X);
                command.Parameters.AddWithValue("@pitch", saveFromInstanceReturn ? MainWorldPosition.World.Rotation.Y : Transform.World.Rotation.Y);
                command.Parameters.AddWithValue("@yaw", saveFromInstanceReturn ? MainWorldPosition.World.Rotation.Z : Transform.World.Rotation.Z);
                command.Parameters.AddWithValue("@faction_id", Faction.Id);
                command.Parameters.AddWithValue("@faction_name", FactionName);
                command.Parameters.AddWithValue("@expedition_id", Expedition?.Id ?? 0);
                command.Parameters.AddWithValue("@family", Family);
                command.Parameters.AddWithValue("@dead_count", DeadCount);
                command.Parameters.AddWithValue("@dead_time", DeadTime);
                command.Parameters.AddWithValue("@rez_wait_duration", RezWaitDuration);
                command.Parameters.AddWithValue("@rez_time", RezTime);
                command.Parameters.AddWithValue("@rez_penalty_duration", RezPenaltyDuration);
                command.Parameters.AddWithValue("@leave_time", LeaveTime);
                command.Parameters.AddWithValue("@money", Money);
                command.Parameters.AddWithValue("@money2", Money2);
                command.Parameters.AddWithValue("@aa_point", AaPoint);
                command.Parameters.AddWithValue("@bank_aa_point", BankAaPoint);
                command.Parameters.AddWithValue("@honor_point", HonorPoint);
                command.Parameters.AddWithValue("@vocation_point", VocationPoint);
                AccumulatePlayTime();
                command.Parameters.AddWithValue("@total_play_time", TotalPlayTime);
                command.Parameters.AddWithValue("@crime_point", CrimePoint);
                command.Parameters.AddWithValue("@crime_record", CrimeRecord);
                command.Parameters.AddWithValue("@jury_point", JuryPoint);
                command.Parameters.AddWithValue("@hostile_faction_kills", HostileFactionKills);
                command.Parameters.AddWithValue("@pvp_honor", HonorGainedInCombat);
                command.Parameters.AddWithValue("@died_in_pvp", DiedInPvp);
                command.Parameters.AddWithValue("@died_in_pvp_war_zone", DiedInPvpWarZone);
                command.Parameters.AddWithValue("@delete_request_time", DeleteRequestTime);
                command.Parameters.AddWithValue("@transfer_request_time", TransferRequestTime);
                command.Parameters.AddWithValue("@delete_time", DeleteTime);
                command.Parameters.AddWithValue("@auto_use_aapoint", AutoUseAAPoint);
                command.Parameters.AddWithValue("@privacy_status", (sbyte)PrivacyStatus);
                command.Parameters.AddWithValue("@prev_point", PrevPoint);
                command.Parameters.AddWithValue("@point", Point);
                command.Parameters.AddWithValue("@gift", Gift);
                command.Parameters.AddWithValue("@num_inv_slot", NumInventorySlots);
                command.Parameters.AddWithValue("@num_bank_slot", NumBankSlots);
                command.Parameters.AddWithValue("@expanded_expert", ExpandedExpert);
                command.Parameters.AddWithValue("@slots", GetActionSlotsAsBlob());
                command.Parameters.AddWithValue("@created_at", Created);
                command.Parameters.AddWithValue("@updated_at", Updated);
                command.Parameters.AddWithValue("@return_district", ReturnDistrictId);
                command.Parameters.AddWithValue("@online_time", OnlineTime.TotalSeconds);
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Connection = connection;
                command.Transaction = transaction;

                foreach (var pair in _options)
                {
                    command.CommandText =
                        "REPLACE INTO `options` (`key`,`value`,`owner`) VALUES (@key,@value,@owner)";
                    command.Parameters.AddWithValue("@key", pair.Key);
                    command.Parameters.AddWithValue("@value", pair.Value);
                    command.Parameters.AddWithValue("@owner", Id);
                    command.ExecuteNonQuery();
                    command.Parameters.Clear();
                }
            }

            // Inventory?.Save(connection, transaction);
            Abilities?.Save(connection, transaction);
            Actability?.Save(connection, transaction);
            Appellations?.Save(connection, transaction);
            // Save active buffs that should persist across logout (SaveRuleId > 0)
            Buffs?.SaveActiveBuffs(connection, transaction, Id);
            Portals?.Save(connection, transaction);
            Friends?.Save(connection, transaction);
            Blocked?.Save(connection, transaction);
            Skills?.Save(connection, transaction);
            Quests?.Save(connection, transaction);
            Mates?.Save(connection, transaction);
            
            result = true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            result = false;
        }

        return result;
    }

    #endregion

    public override void AddVisibleObject(Character character)
    {
        if (this != character) // Never send to self, or the client crashes
        {
            character.SendPacket(new SCUnitStatePacket(this));
            // Initialize the faction transition for newly visible remote characters.
            if (Faction != null && Faction.Id != FactionsEnum.Invalid)
                character.SendPacket(new SCUnitFactionChangedPacket(
                    ObjId, Name ?? "", FactionsEnum.Invalid, Faction.Id, false));
        }
        character.SendPacket(new SCUnitPointsPacket(ObjId, Hp, Mp));
        /*
        // If player is hanging on something, also send a hung packet, this should work in theory, but doesn't
        if (this.Transform.StickyParent != null)
            character.SendPacket(new SCHungPacket(this.ObjId,this.Transform.StickyParent.GameObject.ObjId));
        */
        base.AddVisibleObject(character);
    }

    public override void RemoveVisibleObject(Character character)
    {
        base.RemoveVisibleObject(character);

        if (this != character) // Never send to self, or the client crashes
            character.SendPacket(new SCUnitsRemovedPacket([ObjId]));
    }

    public PacketStream Write(PacketStream stream)
    {
        stream.Write(Id);
        stream.Write(Name);
        stream.Write((byte)Race);
        stream.Write((byte)Gender);
        stream.Write(Level);
        stream.Write(Hp);
        stream.Write(Mp);
        stream.Write(Transform.ZoneId);
        stream.Write((uint)Faction.Id);
        stream.Write(FactionName);
        stream.Write((uint)(Expedition?.Id ?? 0));
        stream.Write(Family);

        var items = Inventory.Equipment.GetSlottedItemsList();
        foreach (var item in items)
        {
            if (item == null)
                stream.Write(0);
            else
                stream.Write(item);
        }

        stream.Write((byte)Ability1);
        stream.Write((byte)Ability2);
        stream.Write((byte)Ability3);

        stream.Write(Helpers.ConvertLongX(Transform.Local.Position.X));
        stream.Write(Helpers.ConvertLongY(Transform.Local.Position.Y));
        stream.Write(Transform.Local.Position.Z);

        stream.Write(ModelParams);
        stream.Write(LaborPower);
        stream.Write(LaborPowerModified);
        stream.Write(DeadCount);
        stream.Write(DeadTime);
        stream.Write(RezWaitDuration);
        stream.Write(RezTime);
        stream.Write(RezPenaltyDuration);
        stream.Write(LeaveTime); // lastWorldLeaveTime
        stream.Write(Money);
        stream.Write(AaPoint);
        stream.Write(CrimePoint); // current crime points (/50)
        stream.Write(CrimeRecord); // total infamy 
        stream.Write((short)0); // crimeScore? trials served?
        stream.Write(DeleteRequestTime);
        stream.Write(TransferRequestTime);
        stream.Write(DeleteTime); // deleteDelay
        stream.Write(ConsumedLaborPower);
        stream.Write(BmPoint); // loyalty tokens
        stream.Write(Money2); // moneyAmount
        stream.Write(BankAaPoint);
        stream.Write(AutoUseAAPoint);
        stream.Write(PrevPoint);
        stream.Write(Point);
        stream.Write(Gift);
        stream.Write(Updated);
        stream.Write((byte)0); // forceNameChange ?
        return stream;
    }

    // Empty equipment (validFlags=0) leaves char-select with "invalid dressing id" and no 3D preview —
    // LoadAccount already fills Inventory; write real gear via EquipmentSerializer (body slots 19-25 are
    // templateId-only). Earlier cause=20 at list build was from money/labor misalignment, not gear itself.
    public PacketStream WriteLobby1013(PacketStream stream)
    {
        // Lobby character record for SC_PACKET_CHARACTER_LIST (opcode 105). Layout matches
        stream.Write((long)Id);                                          // id (i64)
        stream.Write(Name);                                             // name (string)
        stream.Write((byte)Race);                                       // CharRace
        stream.Write((byte)Gender);                                     // CharGender
        stream.Write((byte)Level);                                      // level
        stream.Write(HeirExp);                                         // heirExp (i64)
        stream.Write((uint)Hp);                                        // health
        stream.Write((uint)Mp);                                        // mana
        stream.Write(Transform.ZoneId);                               // zoneId (u32)
        stream.Write((uint)(Faction?.Id ?? 0));                       // factionId (u32)
        stream.Write(FactionName ?? "");                              // factionName (string)
        stream.Write((uint)(Expedition?.Id ?? 0));                    // expeditionId (u32)
        stream.Write((uint)Family);                                   // family (u32)
        EquipmentSerializer.Write(stream, this, BaseUnitType.Character);
        stream.Write((byte)Ability1);                                 // ability1
        stream.Write((byte)Ability2);                                 // ability2
        stream.Write((byte)Ability3);                                 // ability3
        // position record
        stream.Write(Helpers.ConvertLongX(Transform.Local.Position.X)); // x (i64)
        stream.Write(Helpers.ConvertLongY(Transform.Local.Position.Y)); // y (i64)
        stream.Write(Transform.Local.Position.Z);                     // z (float)
        // appearance
        ModelParams.Race = (byte)Race;
        ModelParams.Gender = (byte)Gender;
        ModelParams.VisualRace = (byte)Race;
        ModelParams.VisualGender = (byte)Gender;
        stream.Write(ModelParams);
        stream.Write((short)0);                                       // deadCount (i16)
        stream.Write(0L);                                            // deadTime
        stream.Write((uint)0);                                        // rezWaitDuration
        stream.Write((uint)0);                                        // specialRezWaitDuration
        stream.Write(0L);                                            // rezTime
        stream.Write((uint)0);                                        // rezPenaltyDuration
        stream.Write(0L);                                            // lastWorldLeaveTime
        stream.Write(Money);                                         // moneyAmount (inventory)
        stream.Write(AaPoint);                                       // AA point amount (inventory)
        stream.Write(CrimePoint);                                     // crimePoint (i16)
        stream.Write((int)CrimeRecord);                               // crimeRecord (i32)
        stream.Write((short)0);                                       // crimeScore (i16)
        stream.Write(0L);                                            // deleteRequestedTime
        stream.Write(0L);                                            // transferRequestedTime
        stream.Write(Created);                                       // createdTime
        stream.Write(0L);                                            // deleteDelay
        stream.Write(Money2);                                        // moneyAmount (bank)
        stream.Write(BankAaPoint);                                   // AA point amount (bank)
        stream.Write((byte)(AutoUseAAPoint ? 1 : 0));                 // autoUseAApoint (u8)
        stream.Write((uint)0);                                        // prevPoint
        stream.Write((uint)0);                                        // point
        stream.Write((uint)0);                                        // gift
        stream.Write(0L);                                            // updated
        stream.Write((byte)0);                                        // forceNameChange
        // guid: length-prefixed 16 bytes
        stream.Write(new byte[16], true);
        stream.Write((uint)Math.Max(0, (int)LaborPower));             // lp (account labor cache)
        stream.Write((uint)Math.Max(0, LocalLaborPower));             // localLp
        stream.Write((uint)Math.Max(0, ConsumedLaborPower));          // consumed
        stream.Write(LaborPowerModified);                             // updated (unix DateTime)
        stream.Write(0L);                                             // bmPoint
        stream.Write(0u);                                             // rechargedLp
        stream.Write(0L);                                             // rechargeResetTime
        return stream;
    }

    /// <summary>
    /// Adds crime, and returns the new (current) crime value
    /// </summary>
    /// <param name="amount"></param>
    public void AddCrime(int amount)
    {
        CrimePoint = (short)Math.Clamp((long)CrimePoint + amount, 0L, short.MaxValue);
        CrimeRecord = (int)Math.Clamp((long)CrimeRecord + amount, 0L, int.MaxValue);

        // constructor initializes this reserved i16 field to zero as well.
        SendPacket(new SCCrimeChangedPacket(amount, CrimePoint, CrimeRecord, crimeScore: 0));
    }

    /// <summary>
    /// Called if the player moved, used to handle events that need to happen after the loading screen
    /// </summary>
    public void SetPlayerMoved()
    {
        // Check if it's the first time moving
        if (FinishedLoading)
            return;
        FinishedLoading = true;
        // Skip MOTD SCChatMessage until 10.0.2.13 chat is fully trusted end-to-end.
        // (Saw world + "Welcome to AAEmu!" then DC — A/B: no system chat on first move.)
        var motd = AppConfiguration.Instance.World.MOTD;
        if (!string.IsNullOrWhiteSpace(motd))
            SendMessage(ChatType.System, motd);
    }

    /// <summary>
    /// Restores HP/MP back to their loaded values
    /// </summary>
    public void RestoreSavedHpMp()
    {
        Hp = Math.Min(_savedHp, MaxHp);
        Mp = Math.Min(_savedMp, MaxMp);
    }

    /// <summary>
    /// Handle the "is still in combat" related things
    /// </summary>
    /// <param name="delta"></param>
    protected override void CombatTick(TimeSpan delta)
    {
        // Handle normal combat things
        base.CombatTick(delta);

        // Player specific condition
        if (IsInPostCast && LastCast.AddSeconds(5) < DateTime.UtcNow)
        {
            IsInPostCast = false;
        }
    }

    /// <summary>
    /// Handle player's Breath updates
    /// </summary>
    /// <param name="delta"></param>
    private void BreathTick(TimeSpan delta)
    {
        // The client movement path writes directly to Transform.Local, so the water state has to be
        // refreshed here rather than relying on SetPosition having run.
        UpdateUnderWaterState();

        if (IsDead || !IsUnderWater)
        {
            return;
        }

        // TODO: make this delta-dependant
        if (IsDrowning)
        {
            var damageAmount = MaxHp * .1;
            ReduceCurrentHp(this, (int)damageAmount);
            SendPacket(new SCEnvDamagePacket(EnvSource.Drowning, ObjId, (uint)damageAmount));
        }
        else
        {
            Breath -= 1000; //1 second
            SendPacket(new SCSetBreathPacket(Breath));
        }
    }

    /// <summary>
    /// Call regeneration function of the unit
    /// </summary>
    /// <param name="delta"></param>
    protected override void RegenTick(TimeSpan delta)
    {
        base.RegenTick(delta);

        if (IsDead || !NeedsRegen || IsDrowning)
        {
            return;
        }

        var oldHp = Hp;

        if (IsInBattle)
        {
            Hp += PersistentHpRegen;
        }
        else
        {
            Hp += HpRegen;
        }

        if (IsInPostCast)
        {
            Mp += PersistentMpRegen;
        }
        else
        {
            Mp += MpRegen;
        }

        Hp = Math.Min(Hp, MaxHp);
        Mp = Math.Min(Mp, MaxMp);
        BroadcastPacket(new SCUnitPointsPacket(ObjId, Hp, Mp), true);
        PostUpdateCurrentHp(this, oldHp, Hp, KillReason.Unknown);
    }

    /// <summary>
    /// Check if the player is inactive (crashed or disconnect) and remove the Character object from the world if they did
    /// </summary>
    /// <param name="delta"></param>
    private void CheckPlayerInactivity(TimeSpan delta)
    {
        var maxAllowedInactivityTime = TimeSpan.FromMinutes(2);
        if (DateTime.UtcNow.Subtract(delta) - LastPacketActivityTime > maxAllowedInactivityTime)
        {
            // Kind of prevent repeat calls
            LastPacketActivityTime = DateTime.UtcNow;

            // Remove character
            EnterWorldManager.Instance.LeaveWorldTask(null, LeaveWorldTargetType.CharacterSelect, this);

            // If this character is still linked, then unlink it from the connection
            if (Connection != null && Connection.ActiveChar == this)
            {
                Connection.ActiveChar = null;
                Connection = null;
            }
        }
    }
    
    /// <summary>
    /// Tick called for players, about once per second
    /// </summary>
    /// <param name="delta"></param>    
    public override void OnActiveRegionTick(TimeSpan delta)
    {
        base.OnActiveRegionTick(delta);
        BreathTick(delta);
        CheckPlayerInactivity(delta);
    }

    public override Character GetOwnerCharacter()
    {
        return this;
    }

    public override string DebugName()
    {
        return base.DebugName() + " (" + Id + ")";
    }
}
