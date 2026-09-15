using System.Reflection;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.OpenPortal;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

using ContentPortal = AAEmu.Game.Models.Game.Portal;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

/// <summary>
/// The effect has to reject a portal the client places outside <c>open_portal_effects.distance</c> of
/// its owner before it reaches <c>PortalManager</c>. The old check compared the +X and +Y sides only,
/// so a portal behind the owner or diagonally past the radius was still handed to the manager.
/// </summary>
/// <remarks>
/// content: all 11 rows of open_portal_effects use distance 3.0; the effect's own trace shows a
/// private portal 4097 and a district portal 3 arriving with client-chosen coordinates.
/// </remarks>
[NotInParallel]
public class OpenPortalEffectTests
{
    private const uint PortalId = 4097;
    private const uint SkillId = 11216; // 공간의 문을 여는 중..

    private static readonly FieldInfo PortalManagerInstanceField =
        typeof(Singleton<PortalManager>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;

    private object _previousPortalManager;

    [Before(Test)]
    public void SavePortalManager() => _previousPortalManager = PortalManagerInstanceField.GetValue(null);

    [After(Test)]
    public void RestorePortalManager() => PortalManagerInstanceField.SetValue(null, _previousPortalManager);

    [Test]
    public async Task Apply_PortalPlacedOutOfReach_IsNotHandedToThePortalManager()
    {
        var zoneManager = Mock.Of<IZoneManager>();
        InstallPortalManager(zoneManager.Object);
        var owner = CreateOwner(1000f, 1000f, 100f);
        var effect = new OpenPortalEffect { Distance = 3f };

        // 100 units west / south: only the +X and +Y sides were compared before, so both passed.
        Apply(effect, owner, 900f, 1000f, 100f);
        Apply(effect, owner, 1000f, 900f, 100f);
        // Diagonally: 2.5 on each axis is 3.54 away, past the 3.0 radius but inside every single axis.
        Apply(effect, owner, 1002.5f, 1002.5f, 100f);

        // OpenPortal looks the two zone ids up before it does anything else, so a portal the client
        // placed out of reach never gets that far.
        zoneManager.GetTargetIdByZoneId(Any<uint>()).WasCalled(Times.Never);
    }

    [Test]
    public async Task Apply_PortalWithinReach_IsHandedToThePortalManager()
    {
        var zoneManager = Mock.Of<IZoneManager>();
        InstallPortalManager(zoneManager.Object);

        Apply(new OpenPortalEffect { Distance = 3f }, CreateOwner(1000f, 1000f, 100f), 1002f, 1002f, 100f); // 2.83 away

        // target zone + owner zone.
        zoneManager.GetTargetIdByZoneId(Any<uint>()).WasCalled(Times.Exactly(2));
    }

    [Test]
    public async Task Apply_PortalAboveItsOwner_IsHandedToThePortalManager()
    {
        // The measure is the ground-plane one the rest of the skill code uses, as the old comparison
        // was: a portal placed at the owner's own X/Y opens whatever the height difference.
        var zoneManager = Mock.Of<IZoneManager>();
        InstallPortalManager(zoneManager.Object);

        Apply(new OpenPortalEffect { Distance = 3f }, CreateOwner(1000f, 1000f, 100f), 1000f, 1000f, 250f);

        zoneManager.GetTargetIdByZoneId(Any<uint>()).WasCalled(Times.Exactly(2));
    }

    private static void InstallPortalManager(IZoneManager zoneManager)
    {
        var portalManager = new PortalManager(
            Mock.Of<ILocalizationManager>().Object,
            Mock.Of<IWorldManager>().Object,
            zoneManager,
            Mock.Of<INpcManager>().Object,
            Mock.Of<IObjectIdManager>().Object,
            Mock.Of<ITaskManager>().Object);
        // The reagent tables are loaded from content on startup; emptying them keeps OpenPortal from
        // spawning a portal NPC, so the zone lookup above is the last thing it does.
        SetField(portalManager, "_openPortalInlandReagents", new Dictionary<uint, OpenPortalReagents>());
        SetField(portalManager, "_openPortalOutlandReagents", new Dictionary<uint, OpenPortalReagents>());

        PortalManagerInstanceField.SetValue(null, portalManager);
    }

    private static Character CreateOwner(float x, float y, float z)
    {
        var owner = new Character(new UnitCustomModelParams()) { Id = 7, ObjId = 7, Name = "Owner", Level = 50 };
        owner.Transform.Local.SetPosition(x, y, z, 0f, 0f, 0f);
        // The private portal the client named, so PortalManager can resolve it.
        owner.Portals = new CharacterPortals(owner);
        owner.Portals.PrivatePortals[PortalId] = new ContentPortal { Id = PortalId, Name = "Private", ZoneId = 1 };
        return owner;
    }

    private static void Apply(OpenPortalEffect effect, Character owner, float x, float y, float z) =>
        effect.Apply(owner, new SkillCasterUnit(owner.ObjId), owner, new SkillCastUnitTarget(owner.ObjId),
            new CastSkill(SkillId, 1), new EffectSource(),
            new SkillObjectUnk1 { Type = 1, Id = (int)PortalId, X = x, Y = y, Z = z }, DateTime.UtcNow);

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}
