using System.Reflection;

using AAEmu.Game;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Formulas;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Core.Managers.UnitManagers;

/// <summary>
/// What a preset rebuilds its appearance from. Two kinds of value travel side by side and must not
/// be mixed: the hair palette entry the client resolves against a color table, and the free colour
/// value it applies directly. Alongside them each preset names its own body-image item per slot,
/// including the horn slot, which carries a different asset category from hair and body.
/// </summary>
[NotInParallel]
public sealed class NpcAppearancePresetInputsTests
{
    private const uint ModelId = 9001;
    private const uint TemplateId = 5001;
    private const uint FaceItemId = 6001;
    private const uint HairItemId = 7001;
    private const uint FirstHornItemId = 7101;
    private const uint AlternateHornItemId = 7102;
    private const uint TailItemId = 7201;
    private const uint BodyItemId = 7301;

    // The palette entry the client resolves (a color-table row id) and the free colour the client
    // applies directly. Different domains, deliberately different numbers.
    private const uint HairPaletteId = 111;
    private const uint HornPaletteId = 222;
    private const uint FreeHairColor = 0xFF3366AA;
    private const uint TwoToneHairColor = 0xFF11AA55;

    [Test]
    public async Task Create_KeepsHairPaletteIdAndFreeHairColourInSeparateWireFields()
    {
        using var scope = BuildScope();

        var npc = scope.Create();

        // The palette ids are what the client resolves; neither may be dropped.
        await Assert.That(npc.ModelParams.HairColor).IsEqualTo(HairPaletteId);
        await Assert.That(npc.ModelParams.HornColor).IsEqualTo(HornPaletteId);
        // The free colour travels in its own field, never mixed with the palette id.
        await Assert.That(npc.ModelParams.HairColorId).IsEqualTo(FreeHairColor);
        await Assert.That(npc.ModelParams.HairColorId).IsNotEqualTo(HairPaletteId);
    }

    [Test]
    public async Task Create_CarriesTwoToneColourAndBothWidths()
    {
        using var scope = BuildScope();

        var npc = scope.Create();

        await Assert.That(npc.ModelParams.TwoToneHairColor).IsEqualTo(TwoToneHairColor);
        await Assert.That(npc.ModelParams.TwoToneFirstWidth).IsEqualTo(0.45f);
        await Assert.That(npc.ModelParams.TwoToneSecondWidth).IsEqualTo(0.25f);
    }

    [Test]
    public async Task Create_CarriesAuthoredBodyNormalMapAndWeight()
    {
        using var scope = BuildScope();

        var npc = scope.Create();

        await Assert.That(npc.ModelParams.BodyNormalMap).IsEqualTo(4242u);
        // The authored weight is real content and must survive; a fixed 1 would lose it.
        await Assert.That(npc.ModelParams.BodyWeight).IsEqualTo(0.64f);
    }

    [Test]
    public async Task Create_EquipsTheHornSlotThePresetNames()
    {
        using var scope = BuildScope();

        var npc = scope.Create();

        // The horn slot is a body-image slot of its own, carrying a different asset category. The
        // preset names the item, so that item is what the slot holds - not the model's first part
        // and not an empty slot.
        await Assert.That(Equipment(npc, EquipmentItemSlot.Reserved).TemplateId).IsEqualTo(AlternateHornItemId);
    }

    [Test]
    public async Task Create_EquipsTailAndBodySlotsThePresetNames()
    {
        using var scope = BuildScope();

        var npc = scope.Create();

        await Assert.That(Equipment(npc, EquipmentItemSlot.Tail).TemplateId).IsEqualTo(TailItemId);
        await Assert.That(Equipment(npc, EquipmentItemSlot.Body).TemplateId).IsEqualTo(BodyItemId);
    }

    [Test]
    public async Task Create_FallsBackToTheModelsOwnPartWhenThePresetNamesNone()
    {
        using var scope = BuildScope(namedSlots: false);

        var npc = scope.Create();

        // No named horn: the model still carries horn parts, so the slot takes the first rather
        // than going empty.
        await Assert.That(Equipment(npc, EquipmentItemSlot.Reserved).TemplateId).IsEqualTo(FirstHornItemId);
    }

    [Test]
    public async Task Create_KeepsAPresetNamedIdTheModelDoesNotCarryFromBlankingTheSlot()
    {
        using var scope = BuildScope(hornItemId: 9999);

        var npc = scope.Create();

        // The named horn is not one of the model's parts, so the first part stands. Blanking the
        // slot would silently drop the horn.
        await Assert.That(Equipment(npc, EquipmentItemSlot.Reserved).TemplateId).IsEqualTo(FirstHornItemId);
    }

    [Test]
    public async Task Create_KeepsBodyWeightAtOneWhenThePresetStatesNoWeight()
    {
        using var scope = BuildScope(bodyNormalMapWeight: 0f);

        var npc = scope.Create();

        // No authored weight: one is the neutral value, written per NPC rather than left at zero.
        await Assert.That(npc.ModelParams.BodyWeight).IsEqualTo(1f);
    }

    private static Item Equipment(Npc npc, EquipmentItemSlot slot) =>
        npc.Equipment.GetItemBySlot((int)slot) ?? throw new InvalidOperationException($"Missing {slot}");

    private sealed class Scope : IDisposable
    {
        private readonly SingletonScope<FormulaManager> _formulas;
        private readonly SingletonScope<ItemGameData> _items;
        private readonly SingletonScope<SkillManager> _skills;
        private readonly IDisposable _worlds;

        public bool NamedSlots { get; init; }
        public uint NamedHornItemId { get; init; } = AlternateHornItemId;
        public float BodyNormalMapWeight { get; init; } = 0.64f;

        public Scope(
            SingletonScope<FormulaManager> formulas,
            SingletonScope<ItemGameData> items,
            SingletonScope<SkillManager> skills,
            IDisposable worlds)
        {
            _formulas = formulas;
            _items = items;
            _skills = skills;
            _worlds = worlds;
        }

        public Npc Create()
        {
            var custom = new TotalCharacterCustom
            {
                Id = 101,
                ModelId = ModelId,
                HairId = HairItemId,
                FaceId = FaceItemId,
                // Palette entries (color-table rows), kept distinct from the free colours below.
                HairColorId = HairPaletteId,
                HornColorId = HornPaletteId,
                DefaultHairColor = FreeHairColor,
                TwoToneHairColor = TwoToneHairColor,
                TwoToneFirstWidth = 0.45f,
                TwoToneSecondWidth = 0.25f,
                BodyNormalMapId = 4242,
                BodyNormalMapWeight = BodyNormalMapWeight,
                SkinColorId = 321,
                Modifier = [],
                FaceMovableDecalAssetId = 900
            };
            if (NamedSlots)
            {
                custom.HornId = NamedHornItemId;
                custom.TailId = TailItemId;
                custom.BodyId = BodyItemId;
            }

            var bodyParts = new List<BodyPartTemplate>
            {
                BodyPart(FaceItemId, EquipmentItemSlotType.Face),
                BodyPart(HairItemId, EquipmentItemSlotType.Hair),
                BodyPart(FirstHornItemId, EquipmentItemSlotType.Reserved),
                BodyPart(AlternateHornItemId, EquipmentItemSlotType.Reserved),
                BodyPart(TailItemId, EquipmentItemSlotType.Tail),
                BodyPart(BodyItemId, EquipmentItemSlotType.Body)
            };
            var itemTemplates = bodyParts.Cast<ItemTemplate>().ToDictionary(item => item.Id);

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
            SetPrivateDictionary(manager, "TotalCharacterCustoms", new Dictionary<uint, TotalCharacterCustom> { [custom.Id] = custom });
            SetPrivateDictionary(manager, "ItemBodyParts", BodyPartMap(bodyParts));

            var npc = manager.Create(TestDungeonWorld.CreateWorld(1, 0), 1001, TemplateId);
            return npc ?? throw new InvalidOperationException("Create returned no NPC");
        }

        public void Dispose()
        {
            _worlds.Dispose();
            _skills.Dispose();
            _items.Dispose();
            _formulas.Dispose();
        }
    }

    private static Scope BuildScope(bool namedSlots = true, uint hornItemId = AlternateHornItemId, float bodyNormalMapWeight = 0.64f) =>
        new(InstallNpcFormulas(), InstallEmptyItemGameData(),
            new SingletonScope<SkillManager>(TestManagers.CreateSkillManager()),
            TestDungeonWorld.InstallWorldManager())
        {
            NamedSlots = namedSlots,
            NamedHornItemId = hornItemId,
            BodyNormalMapWeight = bodyNormalMapWeight
        };

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
        foreach (UnitFormulaKind kind in Enum.GetValues<UnitFormulaKind>())
        {
            var formula = new UnitFormula { Id = (uint)kind, Owner = FormulaOwnerType.Npc, Kind = kind };
            formulas[kind] = formula;
            variables[formula.Id] = new Dictionary<UnitFormulaVariableType, Dictionary<uint, UnitFormulaVariable>>
            {
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

    private static BodyPartTemplate BodyPart(uint itemId, EquipmentItemSlotType slotTypeId) =>
        new()
        {
            Id = itemId,
            ItemId = itemId,
            ModelId = ModelId,
            SlotTypeId = (uint)slotTypeId,
            NpcOnly = true
        };

    private static void SetPrivateDictionary<T>(NpcManager manager, string propertyName, Dictionary<uint, T> value)
    {
        var property = typeof(NpcManager).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Missing private property {propertyName}");
        var target = (Dictionary<uint, T>)(property.GetValue(manager)
            ?? throw new InvalidOperationException($"Missing private dictionary {propertyName}"));
        target.Clear();
        foreach (var pair in value)
        {
            target[pair.Key] = pair.Value;
        }
    }

    private static void SetPrivateField(object target, string name, object value)
    {
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                continue;
            }
            field.SetValue(target, value);
            return;
        }
        throw new InvalidOperationException($"No field {name} on {target.GetType().Name}");
    }
}
