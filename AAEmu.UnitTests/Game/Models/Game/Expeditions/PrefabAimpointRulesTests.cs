using System.Numerics;
using AAEmu.Game.Models.Game.Expeditions;

namespace AAEmu.UnitTests.Game.Models.Game.Expeditions;

public class PrefabAimpointRulesTests
{
    private const string SampleLibrary =
        """
        <Prefabs>
          <Prefab Name="ex_h_guild_portal.portal_off">
            <Objects>
              <Object Type="Comment" Name="aimpoint" Pos="0,0,1.6921959"/>
              <Object Type="Brush" Name="mesh" Pos="0,0,-0.01"/>
            </Objects>
          </Prefab>
        </Prefabs>
        """;

    [Test]
    public async Task TrySplitPrefabUri_ParsesShippedWorldgateModel()
    {
        await Assert.That(PrefabAimpointRules.TrySplitPrefabUri(
            "prefab://prefabs/ex_h_guild.xml/ex_h_guild_portal.portal_off",
            out var library, out var name)).IsTrue();
        await Assert.That(library).IsEqualTo("game/prefabs/ex_h_guild.xml");
        await Assert.That(name).IsEqualTo("ex_h_guild_portal.portal_off");
        await Assert.That(PrefabAimpointRules.TrySplitPrefabUri("cgf://objects/x.cgf", out _, out _))
            .IsFalse();
    }

    [Test]
    public async Task TryGetLocalOffset_ReadsAimpointComment()
    {
        await Assert.That(PrefabAimpointRules.TryGetLocalOffset(
            SampleLibrary, "ex_h_guild_portal.portal_off", out var offset)).IsTrue();
        await Assert.That(offset).IsEqualTo(new Vector3(0, 0, 1.6921959f));
        await Assert.That(PrefabAimpointRules.TryGetLocalOffset(
            SampleLibrary, "missing", out _)).IsFalse();
    }
}
