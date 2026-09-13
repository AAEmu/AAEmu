using System.Reflection;
using AAEmu.Commons.Utils;
using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.UnitTests.Utils;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

[NotInParallel]
public class CharacterBackpackDropTests
{
    [Test]
    [Arguments(BackpackType.TradePack)]
    [Arguments(BackpackType.TradeGoods)]
    [Arguments(BackpackType.Fish)]
    [Arguments(BackpackType.CastleClaim)]
    [Arguments(BackpackType.SiegeDeclare)]
    [Arguments(BackpackType.ToyFlag)]
    [Arguments(BackpackType.Instance)]
    public async Task DeathDrop_UsesPhysicalCapabilityAndPreservesOriginalItem(BackpackType type)
    {
        using var context = new Context(type);
        var originalDetail = context.Item.Detail.ToArray();
        var originalFreshness = context.Item.FreshnessStartTime;

        await Assert.That(context.Drop.TryDropOnDeath()).IsTrue();
        await Assert.That(context.Drop.TryDropOnDeath()).IsFalse();

        await Assert.That(context.Owner.Inventory.Equipment.Items).IsEmpty();
        await Assert.That(context.Owner.Inventory.SystemContainer.Items.Single()).IsSameReferenceAs(context.Item);
        await Assert.That(context.Drop.Commits).IsEqualTo(1);
        await Assert.That(context.Drop.Spawns).IsEqualTo(1);
        await Assert.That(context.Doodad.ItemId).IsEqualTo(context.Item.Id);
        await Assert.That(context.Doodad.ItemTemplateId).IsEqualTo(context.Item.TemplateId);
        await Assert.That(context.Doodad.UccId).IsEqualTo(context.Item.UccId);
        await Assert.That(context.Doodad.IsPersistent).IsTrue();
        await Assert.That(context.Doodad.IsPlacementPending).IsFalse();
        await Assert.That(context.Item.MadeUnitId).IsEqualTo(77u);
        await Assert.That(context.Item.Detail).IsEquivalentTo(originalDetail);
        await Assert.That(context.Item.FreshnessStartTime).IsEqualTo(originalFreshness);
        await Assert.That(context.Item.ProductionZoneGroupId).IsEqualTo((ushort)8);
        await Assert.That(context.Drop.PacketsBeforeCommit).IsEqualTo(0);
        await Assert.That(context.Owner.Broadcasts.Any(entry => entry.Packet is SCUnitEquipmentsChangedPacket && entry.Self)).IsTrue();
        await Assert.That(context.Ids.Released).IsEmpty();
    }

    [Test]
    public async Task PendingPlacement_SuppressesStandalonePhaseDataPersistence()
    {
        var doodad = new Doodad { IsPersistent = true, IsPlacementPending = true };
        doodad.Data = 7; // Data's setter normally calls Save().
        doodad.Save();
        doodad.Save(null, null);
        await Assert.That(doodad.DbId).IsEqualTo(0u);
        await Assert.That(doodad.Data).IsEqualTo(7);
    }

    [Test]
    public async Task ConcurrentDeathDrops_CommitOnlyOneGroundPack()
    {
        using var context = new Context(BackpackType.TradeGoods);
        var results = await Task.WhenAll(
            Task.Run(() => context.Drop.TryDropOnDeath()),
            Task.Run(() => context.Drop.TryDropOnDeath()));
        await Assert.That(results.Count(result => result)).IsEqualTo(1);
        await Assert.That(context.Drop.Commits).IsEqualTo(1);
        await Assert.That(context.Drop.Spawns).IsEqualTo(1);
        await Assert.That(context.Owner.Inventory.SystemContainer.Items.Count).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Resolver_DoesNotUseGenericActionFlagAsPhysicalDropPermission(bool flag)
    {
        using var context = new Context(BackpackType.TradeGoods);
        context.Skill.IsDropableBackpack = flag;
        await Assert.That(context.Drop.TryDropOnDeath()).IsTrue();
    }

    [Test]
    [Arguments("glider")]
    [Arguments("missing skill")]
    [Arguments("discard only")]
    [Arguments("zero doodad")]
    [Arguments("ambiguous doodad")]
    [Arguments("not backpack")]
    [Arguments("no equipped item")]
    [Arguments("alive")]
    [Arguments("system full")]
    [Arguments("stacked backpack")]
    public async Task UnsupportedDrop_LeavesInventoryUntouched(string reason)
    {
        using var context = new Context(BackpackType.TradePack);
        switch (reason)
        {
            case "glider": ((BackpackTemplate)context.Item.Template).BackpackType = BackpackType.Glider; break;
            case "missing skill": context.Item.Template.UseSkillId = 999; break;
            case "discard only": context.Skill.Effects.Clear(); context.Item.Template.UseSkillAsReagent = true; break;
            case "zero doodad": ((PutDownBackpackEffect)context.Skill.Effects[0].Template).BackpackDoodadId = 0; break;
            case "ambiguous doodad": context.Skill.Effects.Add(new SkillEffect { Template = new PutDownBackpackEffect { BackpackDoodadId = 51 } }); break;
            case "not backpack": context.Item.Template = new ItemTemplate { Id = 100, UseSkillId = 30 }; break;
            case "no equipped item": context.Owner.Inventory.Equipment.Items.Clear(); break;
            case "alive": context.Owner.Hp = 1; break;
            case "system full": context.Owner.Inventory.SystemContainer.ContainerSize = 0; break;
            case "stacked backpack": context.Item.Count = 2; break;
        }

        await Assert.That(context.Drop.TryDropOnDeath()).IsFalse();
        await Assert.That(context.Owner.Inventory.SystemContainer.Items).IsEmpty();
        await Assert.That(context.Drop.Commits).IsEqualTo(0);
        await Assert.That(context.Drop.Spawns).IsEqualTo(0);
        await Assert.That(context.Owner.Broadcasts).IsEmpty();
        if (reason != "no equipped item")
            await Assert.That(context.Owner.Inventory.Equipment.Items.Single()).IsSameReferenceAs(context.Item);
    }

    [Test]
    public async Task MissingDoodadTemplate_DoesNotMoveItem()
    {
        using var context = new Context(BackpackType.TradeGoods, missingDoodad: true);
        await Assert.That(context.Drop.TryDropOnDeath()).IsFalse();
        await Assert.That(context.Owner.Inventory.GetEquippedBySlot(EquipmentItemSlot.Backpack)).IsSameReferenceAs(context.Item);
        await Assert.That(context.Owner.Inventory.SystemContainer.Items).IsEmpty();
        await Assert.That(context.Ids.Released).IsEmpty();
    }

    [Test]
    [Arguments("initialize")]
    [Arguments("persist false")]
    [Arguments("persist throws")]
    public async Task FailedPlacement_RestoresExactlyOneEquippedPackAndAllowsRetry(string failure)
    {
        using var context = new Context(BackpackType.Fish);
        context.Drop.Failure = failure;
        var originalDetail = context.Item.Detail.ToArray();

        await Assert.That(context.Drop.TryDropOnDeath()).IsFalse();
        await Assert.That(context.Owner.Inventory.Equipment.Items.Single()).IsSameReferenceAs(context.Item);
        await Assert.That(context.Item.SlotType).IsEqualTo(SlotType.Equipment);
        await Assert.That(context.Item.Slot).IsEqualTo((int)EquipmentItemSlot.Backpack);
        await Assert.That(context.Item._holdingContainer).IsSameReferenceAs(context.Owner.Inventory.Equipment);
        await Assert.That(context.Owner.Inventory.SystemContainer.Items).IsEmpty();
        await Assert.That(context.Item.Detail).IsEquivalentTo(originalDetail);
        await Assert.That(context.Owner.Broadcasts).IsEmpty();
        await Assert.That(context.Drop.Spawns).IsEqualTo(0);
        await Assert.That(context.Doodad.ItemId).IsEqualTo(0ul);
        await Assert.That(context.Doodad.IsPersistent).IsFalse();
        await Assert.That(context.Ids.Released).IsEquivalentTo(new uint[] { 40 });

        context.Drop.Failure = null;
        await Assert.That(context.Drop.TryDropOnDeath()).IsTrue();
        await Assert.That(context.Drop.Commits).IsEqualTo(1);
        await Assert.That(context.Owner.Inventory.SystemContainer.Items.Single()).IsSameReferenceAs(context.Item);
    }

    [Test]
    public async Task CommittedDrop_SpawnFailureDoesNotReequipOrSkipZoneNotification()
    {
        using var context = new Context(BackpackType.TradeGoods);
        context.Drop.Failure = "spawn";
        var oldAuthority = WorldIntegration.ZoneAuthority;
        var oldRelay = WorldIntegration.RelayDropBackpackToZone;
        var relays = new List<(Item Item, uint DoodadId, uint ZoneId, bool Remove, bool Hack, bool User)>();
        WorldIntegration.ZoneAuthority = true;
        WorldIntegration.RelayDropBackpackToZone = (_, item, doodad, zone, _, _, _, remove, hack, user) =>
            relays.Add((item, doodad, zone, remove, hack, user));
        try
        {
            await Assert.That(context.Drop.TryDropOnDeath()).IsTrue();
            await Assert.That(context.Drop.TryDropOnDeath()).IsFalse();
            await Assert.That(context.Owner.Inventory.Equipment.Items).IsEmpty();
            await Assert.That(context.Ids.Released).IsEmpty();
            await Assert.That(relays.Count).IsEqualTo(1);
            await Assert.That(relays[0].Item).IsSameReferenceAs(context.Item);
            await Assert.That(relays[0].DoodadId).IsEqualTo(50u);
            await Assert.That(relays[0].ZoneId).IsEqualTo(70u);
            await Assert.That(relays[0].Remove).IsTrue();
            await Assert.That(relays[0].Hack).IsFalse();
            await Assert.That(relays[0].User).IsFalse();
        }
        finally
        {
            WorldIntegration.ZoneAuthority = oldAuthority;
            WorldIntegration.RelayDropBackpackToZone = oldRelay;
        }
    }

    [Test]
    public async Task AttachedCharacter_DropsAtDetachedWorldPosition()
    {
        using var context = new Context(BackpackType.TradePack);
        var parent = new GameObject();
        parent.Transform.Local.SetPosition(100, 200, 30);
        SetField(context.Owner.Transform, "_parentTransform", parent.Transform);
        context.Owner.Transform.Local.SetPosition(2, 3, 4);
        await Assert.That(context.Drop.TryDropOnDeath()).IsTrue();
        await Assert.That(context.Doodad.Transform.Parent).IsNull();
        await Assert.That(context.Doodad.Transform.World.Position.X).IsEqualTo(102f);
        await Assert.That(context.Doodad.Transform.World.Position.Y).IsEqualTo(203f);
        await Assert.That(context.Doodad.Transform.World.Position.Z).IsEqualTo(12.5f);
    }

    [Test]
    [Arguments("npc")]
    [Arguments("player")]
    [Arguments("environment")]
    [Arguments("death callback failure")]
    [Arguments("placement failure")]
    public async Task CharacterDeath_InvokesDropAndCompletesCleanup(string cause)
    {
        using var context = new Context(BackpackType.TradeGoods);
        using var items = new SingletonScope<ItemManager>(CreateItemManager());
        var formulas = new FormulaManager();
        SetField(formulas, "_formulas", new Dictionary<uint, Formula>());
        using var formulaScope = new SingletonScope<FormulaManager>(formulas);
        var zones = new ZoneManager(Mock.Of<IWorldManager>().Object);
        SetField(zones, "_zones", new Dictionary<uint, Zone>());
        using var zoneScope = new SingletonScope<ZoneManager>(zones);
        using var expeditions = new SingletonScope<ExpeditionManager>(new ExpeditionManager(
            Mock.Of<IExpeditionIdManager>().Object, Mock.Of<ITeamManager>().Object,
            Mock.Of<IWorldManager>().Object, Mock.Of<IChatManager>().Object));
        var oldAuthority = WorldIntegration.ZoneAuthority;
        var oldDeathRelay = WorldIntegration.RelayUnitDeathToZone;
        var oldDropRelay = WorldIntegration.RelayDropBackpackToZone;
        var deathRelays = 0;
        WorldIntegration.ZoneAuthority = true;
        WorldIntegration.RelayDropBackpackToZone = null;
        WorldIntegration.RelayUnitDeathToZone = _ => deathRelays++;
        if (cause == "death callback failure")
            context.Owner.Events.OnDeath += (_, _) => throw new IOException("Death observer failed");
        if (cause == "placement failure")
            context.Drop.Failure = "persist false";
        Unit killer = cause switch
        {
            "environment" => context.Owner,
            "player" => new CharacterMock { Id = 2, ObjId = 2 },
            _ => new SilentNpc { ObjId = 2 }
        };
        try
        {
            Action die = () => context.Owner.DoDie(killer, KillReason.Damage);
            if (cause == "death callback failure")
                await Assert.That(die).Throws<IOException>();
            else
                die();
            await Assert.That(context.Drop.Commits).IsEqualTo(cause == "placement failure" ? 0 : 1);
            await Assert.That(context.Owner.CleanupCalls).IsEqualTo(1);
            await Assert.That(deathRelays).IsEqualTo(1);
            if (cause == "environment")
                await Assert.That(context.Owner.HostileFactionKills).IsEqualTo(0u);
        }
        finally
        {
            WorldIntegration.ZoneAuthority = oldAuthority;
            WorldIntegration.RelayUnitDeathToZone = oldDeathRelay;
            WorldIntegration.RelayDropBackpackToZone = oldDropRelay;
        }
    }

    private sealed class Context : IDisposable
    {
        public DeathCharacter Owner { get; } = new() { Id = 1, ObjId = 1 };
        public Backpack Item { get; }
        public Doodad Doodad { get; }
        public SkillTemplate Skill { get; }
        public TestDrop Drop { get; }
        public RecordingIds Ids { get; } = new();
        private readonly WorldInstance _world = new(new WorldTemplate { Id = 1, Name = "death-drop" }, 0, true, 1);

        public Context(BackpackType type, bool missingDoodad = false)
        {
            SetField(Owner, "_parentWorld", _world);
            SetField(Owner.Transform, "_zoneId", 70u);
            _world.MateManager = new MateManager(_world);
            var inventory = DetachedInventory.Create(Owner);
            inventory.Equipment.ContainerSize = 32;
            Item = new Backpack(10, new BackpackTemplate { Id = 100, UseSkillId = 30, BackpackType = type, MaxCount = 1 }, 1)
            {
                SlotType = SlotType.Equipment, Slot = (int)EquipmentItemSlot.Backpack,
                OwnerId = Owner.Id, MadeUnitId = 77, UccId = 99, _holdingContainer = inventory.Equipment
            };
            Item.InitializeFreshness(new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc), 8);
            inventory.Equipment.Items.Add(Item);
            Doodad = new Doodad { ObjId = 40, TemplateId = 50 };
            SetField(Doodad, "_parentWorld", _world);
            Skill = new SkillTemplate
            {
                Id = 30, IsDropableBackpack = false,
                Effects = [new SkillEffect { Template = new PutDownBackpackEffect { BackpackDoodadId = 50 } }]
            };
            var skills = Mock.Of<ISkillManager>();
            skills.GetSkillTemplate(30).Returns(Skill);
            var doodads = Mock.Of<IDoodadManager>();
            doodads.Create(_world, 0, 50, Owner, true).Returns(missingDoodad ? null : Doodad);
            Drop = new TestDrop(Owner, skills.Object, doodads.Object, Ids, Mock.Of<IMailManager>().Object);
            Owner.Drop = Drop;
        }

        public void Dispose() => _world.Dispose();
    }

    private sealed class TestDrop(DeathCharacter owner, ISkillManager skills, IDoodadManager doodads,
        INonUnitObjectIdManager ids, IMailManager mail) : CharacterBackpackDrop(owner, skills, doodads, ids, mail)
    {
        public string Failure { get; set; }
        public int Commits { get; private set; }
        public int Spawns { get; private set; }
        public int PacketsBeforeCommit { get; private set; }
        protected override float GetGroundHeight(Doodad doodad) => 12.5f;
        protected override void Initialize(Doodad doodad)
        {
            if (!doodad.IsPlacementPending) throw new InvalidOperationException("Initialization is outside placement transaction scope");
            doodad.Data = 7;
            if (Failure == "initialize") throw new IOException("Initialization failed");
        }
        protected override bool Persist(Item item, Doodad doodad)
        {
            if (Failure == "persist throws") throw new IOException("Commit failed");
            if (Failure == "persist false") return false;
            if (item.SlotType != SlotType.System || doodad.ItemId != item.Id || !doodad.IsPersistent || !doodad.IsPlacementPending)
                throw new InvalidOperationException("Incomplete staged placement");
            PacketsBeforeCommit = owner.Broadcasts.Count;
            Commits++;
            return true;
        }
        protected override void Spawn(Doodad doodad)
        {
            if (Commits == 0) throw new InvalidOperationException("Spawn before commit");
            if (Failure == "spawn") throw new IOException("Spawn notification failed");
            Spawns++;
        }
    }

    private sealed class DeathCharacter : CharacterMock
    {
        public CharacterBackpackDrop Drop { get; set; }
        public List<(GamePacket Packet, bool Self)> Broadcasts { get; } = [];
        public int CleanupCalls { get; private set; }
        protected override void DropBackpackOnDeath() => Drop.TryDropOnDeath();
        public override void ClearAllAggro() => CleanupCalls++;
        public override void BroadcastPacket(GamePacket packet, bool self) => Broadcasts.Add((packet, self));
    }

    private sealed class SilentNpc : Npc
    {
        public override void BroadcastPacket(GamePacket packet, bool self) { }
    }

    private sealed class RecordingIds : INonUnitObjectIdManager
    {
        public List<uint> Released { get; } = [];
        public void Load() { }
        public bool Initialize(bool forceReset = false) => true;
        public uint GetNextId() => 40;
        public uint[] GetNextId(int count) => Enumerable.Repeat(40u, count).ToArray();
        public void ReleaseId(uint id) => Released.Add(id);
        public void ReleaseId(IEnumerable<uint> ids) => Released.AddRange(ids);
    }

    private static ItemManager CreateItemManager()
    {
        var manager = new ItemManager(Mock.Of<ISkillManager>().Object, Mock.Of<IItemIdManager>().Object,
            Mock.Of<IContainerIdManager>().Object, Mock.Of<ILocalizationManager>().Object,
            Mock.Of<ITaskManager>().Object, Mock.Of<IWorldManager>().Object);
        SetField(manager, "_config", new ItemConfig());
        return manager;
    }

    private static void SetField(object target, string name, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) continue;
            field.SetValue(target, value);
            return;
        }
        throw new InvalidOperationException($"Missing field {name}");
    }

    private sealed class SingletonScope<T> : IDisposable where T : class
    {
        private readonly FieldInfo _field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        private readonly object _previous;
        public SingletonScope(T value)
        {
            _previous = _field.GetValue(null);
            _field.SetValue(null, value);
        }
        public void Dispose() => _field.SetValue(null, _previous);
    }
}
