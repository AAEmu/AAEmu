using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Core.Managers.UnitManagers;

[NotInParallel]
public sealed class NpcAppearanceReconstructionTests
{
    private const uint ModelId = 9001;
    private const uint FaceItemId = 6001;
    private const uint FallbackFaceItemId = 6002;
    private const uint HairItemId = 7001;
    private const uint AlternateHairItemId = 7002;
    private const uint BeardItemId = 7101;
    private const uint BodyItemId = 7201;
    private const uint GlassesItemId = 7301;
    private const uint TailItemId = 7401;
    private const uint CosplayItemId = 8001;
    private const uint TemplateId = 5001;

    [Test]
    public async Task Create_UsesModelToRebuildFaceHairBodyAndCosplaySlots()
    {
        using var formulas = InstallNpcFormulas();
        using var itemData = InstallEmptyItemGameData();
        using var skillManager = new SingletonScope<SkillManager>(TestManagers.CreateSkillManager());
        using var worldManager = TestDungeonWorld.InstallWorldManager();
        var (manager, _, world) = BuildManager(
        [
            Custom(101, HairItemId, FaceItemId, 111, 121),
        ]);

        var npc = manager.Create(world, 1001, TemplateId);

        await Assert.That(npc).IsNotNull();
        await Assert.That(npc.ModelId).IsEqualTo(ModelId);
        await Assert.That(npc.ModelParams.Face).IsNotNull();
        await Assert.That(npc.ModelParams.Face.MovableDecalAssetId).IsEqualTo(1001u);
        await Assert.That(npc.ModelParams.HairColorId).IsEqualTo(111u);
        await Assert.That(npc.ModelParams.SkinColorId).IsEqualTo(121u);

        await Assert.That(Equipment(npc, EquipmentItemSlot.Face).TemplateId).IsEqualTo(FaceItemId);
        await Assert.That(Equipment(npc, EquipmentItemSlot.Hair).TemplateId).IsEqualTo(HairItemId);
        await Assert.That(Equipment(npc, EquipmentItemSlot.Beard).TemplateId).IsEqualTo(BeardItemId);
        await Assert.That(Equipment(npc, EquipmentItemSlot.Body).TemplateId).IsEqualTo(BodyItemId);
        await Assert.That(Equipment(npc, EquipmentItemSlot.Glasses).TemplateId).IsEqualTo(GlassesItemId);
        await Assert.That(Equipment(npc, EquipmentItemSlot.Tail).TemplateId).IsEqualTo(TailItemId);
        await Assert.That(Equipment(npc, EquipmentItemSlot.Cosplay).TemplateId).IsEqualTo(CosplayItemId);
    }

    [Test]
    public async Task Create_RepeatedSpawnsKeepIndependentModelParamsAndEquipment()
    {
        using var formulas = InstallNpcFormulas();
        using var itemData = InstallEmptyItemGameData();
        using var skillManager = new SingletonScope<SkillManager>(TestManagers.CreateSkillManager());
        using var worldManager = TestDungeonWorld.InstallWorldManager();
        var (manager, template, world) = BuildManager(
        [
            Custom(101, HairItemId, FaceItemId, 111, 121),
        ]);

        var first = manager.Create(world, 1001, TemplateId);
        var second = manager.Create(world, 1002, TemplateId);

        await Assert.That(first).IsNotNull();
        await Assert.That(second).IsNotNull();
        await Assert.That(ReferenceEquals(first.ModelParams, second.ModelParams)).IsFalse();
        await Assert.That(ReferenceEquals(first.ModelParams, template.ModelParams)).IsFalse();
        await Assert.That(first.ModelParams.HairColorId).IsEqualTo(111u);
        await Assert.That(second.ModelParams.HairColorId).IsEqualTo(111u);
        await Assert.That(Equipment(first, EquipmentItemSlot.Hair).TemplateId).IsEqualTo(HairItemId);
        await Assert.That(Equipment(second, EquipmentItemSlot.Hair).TemplateId).IsEqualTo(HairItemId);

        first.ModelParams.HairColorId = 999;
        first.Equipment.GetItemBySlot((int)EquipmentItemSlot.Hair).Grade = 7;
        await Assert.That(second.ModelParams.HairColorId).IsEqualTo(111u);
        await Assert.That(Equipment(second, EquipmentItemSlot.Hair).Grade).IsEqualTo((byte)0);
        await Assert.That(template.ModelParams.HairColorId).IsEqualTo(0u);
    }

    [Test]
    public async Task Create_ControlledRandomSelectsCompatibleCustomWithoutMutatingTemplate()
    {
        using var formulas = InstallNpcFormulas();
        using var itemData = InstallEmptyItemGameData();
        using var skillManager = new SingletonScope<SkillManager>(TestManagers.CreateSkillManager());
        using var worldManager = TestDungeonWorld.InstallWorldManager();
        var (manager, template, world) = BuildManager(
        [
            Custom(101, HairItemId, FaceItemId, 111, 121),
            Custom(102, AlternateHairItemId, FallbackFaceItemId, 112, 122),
        ]);
        var originalModelParams = template.ModelParams;
        var originalBodyHair = template.BodyItems[(int)EquipmentItemSlotType.Hair - (int)EquipmentItemSlotType.Face];
        SetLoadCustomRandom(manager, new FixedIndexRandom(1));

        var npc = manager.Create(world, 1001, TemplateId);

        await Assert.That(npc).IsNotNull();
        await Assert.That(npc.ModelParams.HairColorId).IsEqualTo(112u);
        await Assert.That(npc.ModelParams.SkinColorId).IsEqualTo(122u);
        await Assert.That(npc.ModelParams.Face.MovableDecalAssetId).IsEqualTo(1002u);
        await Assert.That(Equipment(npc, EquipmentItemSlot.Hair).TemplateId).IsEqualTo(AlternateHairItemId);
        await Assert.That(Equipment(npc, EquipmentItemSlot.Face).TemplateId).IsEqualTo(FallbackFaceItemId);
        await Assert.That(ReferenceEquals(template.ModelParams, originalModelParams)).IsTrue();
        await Assert.That(template.ModelParams.HairColorId).IsEqualTo(0u);
        await Assert.That(template.HairId).IsEqualTo(0u);
        await Assert.That(template.BodyItems[(int)EquipmentItemSlotType.Hair - (int)EquipmentItemSlotType.Face]).IsEquivalentTo(originalBodyHair);
    }

    [Test]
    public async Task Create_ExplicitTotalCustomUsesTemplateLookWithoutRandomSelection()
    {
        using var formulas = InstallNpcFormulas();
        using var itemData = InstallEmptyItemGameData();
        using var skillManager = new SingletonScope<SkillManager>(TestManagers.CreateSkillManager());
        using var worldManager = TestDungeonWorld.InstallWorldManager();
        var (manager, template, world) = BuildManager(
        [
            Custom(101, HairItemId, FaceItemId, 111, 121),
            Custom(102, AlternateHairItemId, FallbackFaceItemId, 112, 122),
        ]);
        template.TotalCustomId = 101;
        template.HairId = HairItemId;
        template.ModelParams = FaceParams(777, 888, 999, 1001);
        SetBodySlots(template);

        var npc = manager.Create(world, 1001, TemplateId);

        await Assert.That(npc).IsNotNull();
        await Assert.That(npc.ModelId).IsEqualTo(ModelId);
        await Assert.That(npc.ModelParams.HairColorId).IsEqualTo(888u);
        await Assert.That(npc.ModelParams.SkinColorId).IsEqualTo(999u);
        await Assert.That(npc.ModelParams.Face.MovableDecalAssetId).IsEqualTo(1001u);
        await Assert.That(Equipment(npc, EquipmentItemSlot.Hair).TemplateId).IsEqualTo(HairItemId);
        await Assert.That(Equipment(npc, EquipmentItemSlot.Face).TemplateId).IsEqualTo(FaceItemId);
    }

    [Test]
    public async Task Create_UnitStateAndWzNpcStateKeepAppearanceIdsStable()
    {
        using var formulas = InstallNpcFormulas();
        using var itemData = InstallEmptyItemGameData();
        using var skillManager = new SingletonScope<SkillManager>(TestManagers.CreateSkillManager());
        using var worldManager = TestDungeonWorld.InstallWorldManager();
        var (manager, _, world) = BuildManager(
        [
            Custom(101, HairItemId, FaceItemId, 111, 121),
        ]);
        var npc = manager.Create(world, 1001, TemplateId);

        var unitStateA = new SCUnitStatePacket(npc).Write(new PacketStream()).GetBytes();
        var unitStateB = new SCUnitStatePacket(npc).Write(new PacketStream()).GetBytes();
        var wzStateA = BuildWzNpcStateBody(npc);
        var wzStateB = BuildWzNpcStateBody(npc);

        await Assert.That(Convert.ToHexString(unitStateB)).IsEqualTo(Convert.ToHexString(unitStateA));
        await Assert.That(Convert.ToHexString(wzStateB)).IsEqualTo(Convert.ToHexString(wzStateA));
        await Assert.That(ContainsUInt32(unitStateA, ModelId)).IsTrue();
        await Assert.That(ContainsUInt32(unitStateA, FaceItemId)).IsTrue();
        await Assert.That(ContainsUInt32(unitStateA, HairItemId)).IsTrue();
        await Assert.That(ContainsUInt32(unitStateA, CosplayItemId)).IsTrue();
        await Assert.That(ContainsUInt32(wzStateA, FaceItemId)).IsTrue();
        await Assert.That(ContainsUInt32(wzStateA, HairItemId)).IsTrue();
        await Assert.That(ContainsUInt32(wzStateA, CosplayItemId)).IsTrue();
    }

    private static SingletonScope<ItemGameData> InstallEmptyItemGameData()
    {
        var manager = new ItemGameData();
        var field = typeof(ItemGameData).GetField("_itemGradeBuffs", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing ItemGameData item-grade table");
        field.SetValue(manager, Activator.CreateInstance(field.FieldType));
        return new SingletonScope<ItemGameData>(manager);
    }

    private static SingletonScope<FormulaManager> InstallNpcFormulas()
    {
        var manager = new FormulaManager();
        var formulas = new Dictionary<UnitFormulaKind, UnitFormula>();
        var variables = new Dictionary<uint, Dictionary<UnitFormulaVariableType, Dictionary<uint, UnitFormulaVariable>>>();
        var kinds = new[]
        {
            UnitFormulaKind.Str,
            UnitFormulaKind.Dex,
            UnitFormulaKind.Sta,
            UnitFormulaKind.Int,
            UnitFormulaKind.Spi,
            UnitFormulaKind.Fai,
            UnitFormulaKind.MaxHealth,
            UnitFormulaKind.MaxMana
        };

        uint id = 1;
        foreach (var kind in kinds)
        {
            var formula = new UnitFormula
            {
                Id = id++,
                Kind = kind,
                Owner = FormulaOwnerType.Npc,
                TextFormula = "1"
            };
            formula.Prepare();
            formulas[kind] = formula;
            variables[formula.Id] = new Dictionary<UnitFormulaVariableType, Dictionary<uint, UnitFormulaVariable>>
            {
                [UnitFormulaVariableType.NpcTemplate] = Keyed(formula.Id, UnitFormulaVariableType.NpcTemplate, (byte)NpcTemplateType.Default),
                [UnitFormulaVariableType.NpcKind] = Keyed(formula.Id, UnitFormulaVariableType.NpcKind, (byte)NpcKindType.Human),
                [UnitFormulaVariableType.NpcGrade] = Keyed(formula.Id, UnitFormulaVariableType.NpcGrade, (byte)NpcGradeType.Normal)
            };
        }

        SetPrivateField(manager, "_unitFormulas", new Dictionary<FormulaOwnerType, Dictionary<UnitFormulaKind, UnitFormula>>
        {
            [FormulaOwnerType.Npc] = formulas
        });
        SetPrivateField(manager, "_unitVariables", variables);
        return new SingletonScope<FormulaManager>(manager);
    }

    private static Dictionary<uint, UnitFormulaVariable> Keyed(uint formulaId, UnitFormulaVariableType type, uint key) =>
        new()
        {
            [key] = new UnitFormulaVariable { FormulaId = formulaId, Type = type, Key = key, Value = 1f }
        };

    private static (NpcManager Manager, NpcTemplate Template, WorldInstance World) BuildManager(
        IReadOnlyCollection<TotalCharacterCustom> customs)
    {
        var bodyParts = new List<BodyPartTemplate>
        {
            BodyPart(FaceItemId, (uint)EquipmentItemSlotType.Face),
            BodyPart(FallbackFaceItemId, (uint)EquipmentItemSlotType.Face),
            BodyPart(HairItemId, (uint)EquipmentItemSlotType.Hair),
            BodyPart(AlternateHairItemId, (uint)EquipmentItemSlotType.Hair),
            BodyPart(BeardItemId, (uint)EquipmentItemSlotType.Beard),
            BodyPart(BodyItemId, (uint)EquipmentItemSlotType.Body),
            BodyPart(GlassesItemId, (uint)EquipmentItemSlotType.Glasses),
            BodyPart(TailItemId, (uint)EquipmentItemSlotType.Tail),
        };
        var cosplay = new ArmorTemplate { Id = CosplayItemId };
        var itemTemplates = bodyParts.Cast<ItemTemplate>().Append(cosplay).ToDictionary(item => item.Id);

        var model = Mock.Of<IModelManager>();
        model.GetActorModel(Any<uint>()).Returns(new ActorModel { Id = ModelId });
        model.GetModelType(Any<uint>()).Returns(new ModelType { SubType = "ActorModel" });

        var faction = Mock.Of<IFactionManager>();
        faction.GetFaction(Any<FactionsEnum>()).Returns(new SystemFaction { Id = FactionsEnum.Neutral });

        var itemManager = Mock.Of<IItemManager>();
        itemManager.GetAllItems().Returns(bodyParts.Cast<ItemTemplate>().ToList());
        itemManager.GetTemplate(Any<uint>()).Returns((uint id) => itemTemplates.GetValueOrDefault(id));
        itemManager.Create(Any<uint>(), Any<int>(), Any<byte>(), Any<bool>())
            .Returns((uint id, int count, byte grade, bool _) =>
                new Item((byte)1, (ulong)id, itemTemplates[id], count) { Grade = grade });

        var manager = new NpcManager(
            Mock.Of<IObjectIdManager>().Object,
            model.Object,
            faction.Object,
            itemManager.Object,
            Mock.Of<ITaskManager>().Object);

        var template = new NpcTemplate
        {
            Id = TemplateId,
            ModelId = ModelId,
            Level = 1,
            FactionId = FactionsEnum.Neutral,
            NpcTemplateId = NpcTemplateType.Default,
            NpcKindId = NpcKindType.Human,
            NpcGradeId = NpcGradeType.Normal,
            UsesCharacterAppearance = true,
            Race = 1,
            Gender = 2,
            DefaultFaceItemId = FallbackFaceItemId,
            Items = new EquipItemsTemplate { Cosplay = CosplayItemId },
            ModelParams = new UnitCustomModelParams(UnitCustomModelType.Skin)
            {
                Race = 1,
                Gender = 2,
                VisualRace = 1,
                VisualGender = 2,
                BodyWeight = 1f,
                ModelId = ModelId
            }
        };
        manager.GetAllTemplates().Add(template.Id, template);
        SetPrivateDictionary(manager, "TotalCharacterCustoms", customs.ToDictionary(custom => custom.Id));
        SetPrivateDictionary(manager, "ItemBodyParts", BodyPartMap(bodyParts));
        SetBodySlots(template);

        var world = TestDungeonWorld.CreateWorld(1, 0);
        return (manager, template, world);
    }

    private static Dictionary<uint, Dictionary<uint, List<BodyPartTemplate>>> BodyPartMap(
        IEnumerable<BodyPartTemplate> parts)
    {
        var result = new Dictionary<uint, Dictionary<uint, List<BodyPartTemplate>>>();
        foreach (var part in parts)
        {
            if (!result.TryGetValue(ModelId, out var slots))
            {
                slots = [];
                result[ModelId] = slots;
            }
            if (!slots.TryGetValue(part.SlotTypeId, out var list))
            {
                list = [];
                slots[part.SlotTypeId] = list;
            }
            list.Add(part);
        }
        return result;
    }

    private static BodyPartTemplate BodyPart(uint itemId, uint slotTypeId) =>
        new()
        {
            Id = itemId,
            ItemId = itemId,
            ModelId = ModelId,
            SlotTypeId = slotTypeId,
            NpcOnly = true
        };

    private static TotalCharacterCustom Custom(uint id, uint hairId, uint faceId, uint hairColor, uint skinColor) =>
        new()
        {
            Id = id,
            ModelId = ModelId,
            HairId = hairId,
            FaceId = faceId,
            HairColorId = hairColor,
            SkinColorId = skinColor,
            FaceMovableDecalAssetId = 900 + id,
            Modifier = []
        };

    private static UnitCustomModelParams FaceParams(uint modelId, uint hairColor, uint skinColor, uint decal) =>
        new(UnitCustomModelType.Face)
        {
            ModelId = modelId,
            HairColorId = hairColor,
            SkinColorId = skinColor,
            Face =
            {
                MovableDecalAssetId = decal
            }
        };

    private static void SetBodySlots(NpcTemplate template)
    {
        template.BodyItems[(int)EquipmentItemSlotType.Face - (int)EquipmentItemSlotType.Face] = (FaceItemId, true);
        template.BodyItems[(int)EquipmentItemSlotType.Hair - (int)EquipmentItemSlotType.Face] = (HairItemId, true);
        template.BodyItems[(int)EquipmentItemSlotType.Beard - (int)EquipmentItemSlotType.Face] = (BeardItemId, true);
        template.BodyItems[(int)EquipmentItemSlotType.Body - (int)EquipmentItemSlotType.Face] = (BodyItemId, true);
        template.BodyItems[(int)EquipmentItemSlotType.Glasses - (int)EquipmentItemSlotType.Face] = (GlassesItemId, true);
        template.BodyItems[(int)EquipmentItemSlotType.Tail - (int)EquipmentItemSlotType.Face] = (TailItemId, true);
    }

    private static void SetPrivateDictionary<T>(NpcManager manager, string propertyName, Dictionary<uint, T> value)
    {
        var property = typeof(NpcManager).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing private property {propertyName}");
        var target = (Dictionary<uint, T>)(property.GetValue(manager)
            ?? throw new InvalidOperationException($"Missing private dictionary {propertyName}"));
        target.Clear();
        foreach (var pair in value)
            target[pair.Key] = pair.Value;
    }

    private static void SetPrivateField(object target, string name, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                continue;
            field.SetValue(target, value);
            return;
        }
        throw new InvalidOperationException($"No field {name} on {target.GetType().Name}");
    }

    private static Item Equipment(Npc npc, EquipmentItemSlot slot) =>
        npc.Equipment.GetItemBySlot((int)slot) ?? throw new InvalidOperationException($"Missing {slot}");

    private static bool ContainsUInt32(byte[] bytes, uint value)
    {
        var pattern = BitConverter.GetBytes(value);
        for (var i = 0; i <= bytes.Length - pattern.Length; i++)
        {
            if (bytes.AsSpan(i, pattern.Length).SequenceEqual(pattern))
                return true;
        }
        return false;
    }

    private static void SetLoadCustomRandom(NpcManager manager, Random random)
    {
        var field = typeof(NpcManager).GetField("_loadCustomRandom", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing NpcManager custom-look random seam");
        field.SetValue(manager, random);
    }

    private sealed class FixedIndexRandom : Random
    {
        private readonly int _index;

        public FixedIndexRandom(int index)
        {
            _index = index;
        }

        public override int Next(int maxValue) => _index;
    }

    private static byte[] BuildWzNpcStateBody(Npc npc)
    {
        var metadataType = typeof(WorldIntegration).GetNestedType("WzNpcSpawnMetadata", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing WZ NPC spawn metadata type");
        var constructor = metadataType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
        var metadata = constructor.Invoke(
        [
            0u,
            (byte)0,
            (byte)0,
            (ushort)0,
            NpcSpawnReasonType.Default,
            null,
            0f,
            0u,
            0u,
            (byte)0
        ]);
        var method = typeof(WorldIntegration)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == "BuildWzNpcStateBody" && candidate.GetParameters().Length == 8);

        return (byte[])method.Invoke(null,
        [
            npc,
            metadata,
            null,
            0f,
            false,
            false,
            null,
            false
        ])!;
    }
}
