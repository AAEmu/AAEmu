using AAEmu.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Scripts.Commands;

namespace AAEmu.UnitTests.Game.Scripts.Commands;

[NotInParallel]
public class ReviveZoneSynchronizationTests
{
    [Test]
    public async Task Revive_ZoneAuthority_RelaysResurrectionCombatClearAndPointsInOrder()
    {
        var previousAuthority = WorldIntegration.ZoneAuthority;
        var previousResurrection = WorldIntegration.RelayUnitResurrectionToZone;
        var previousCombatClear = WorldIntegration.RelayCombatClearedToZone;
        var previousPoints = WorldIntegration.RelayUnitPointsToZone;
        var calls = new List<string>();
        var character = new TestCharacter()
        {
            ObjId = 1570,
            Hp = 0,
            Mp = 0
        };
        character.Transform.Local.SetPosition(10f, 20f, 30f, 0f, 0f, 1f);

        try
        {
            WorldIntegration.ZoneAuthority = true;
            WorldIntegration.RelayUnitResurrectionToZone = (id, x, y, z, rotation) =>
                calls.Add($"resurrection:{id}:{x}:{y}:{z}:{rotation}");
            WorldIntegration.RelayCombatClearedToZone = id => calls.Add($"clear:{id}");
            WorldIntegration.RelayUnitPointsToZone = (id, hp, mp) => calls.Add($"points:{id}:{hp}:{mp}");

            Revive.ReviveCharacter(character);

            await Assert.That(calls.Count).IsEqualTo(3);
            await Assert.That(calls[0]).IsEqualTo("resurrection:1570:10:20:30:1");
            await Assert.That(calls[1]).IsEqualTo("clear:1570");
            await Assert.That(calls[2]).IsEqualTo("points:1570:370:125");
            await Assert.That(character.Hp).IsEqualTo(370);
            await Assert.That(character.Mp).IsEqualTo(125);
        }
        finally
        {
            WorldIntegration.ZoneAuthority = previousAuthority;
            WorldIntegration.RelayUnitResurrectionToZone = previousResurrection;
            WorldIntegration.RelayCombatClearedToZone = previousCombatClear;
            WorldIntegration.RelayUnitPointsToZone = previousPoints;
        }
    }

    private sealed class TestCharacter() : Character(new UnitCustomModelParams())
    {
        public override int MaxHp => 370;
        public override int MaxMp => 125;
    }
}
