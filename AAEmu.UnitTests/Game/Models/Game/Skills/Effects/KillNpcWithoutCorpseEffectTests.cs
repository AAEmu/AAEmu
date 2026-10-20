using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects;

public class KillNpcWithoutCorpseEffectTests
{
    [Test]
    public async Task GetRemovalTargets_DifferentCasterAndRepeatedNeighbors_IncludesTargetOnce()
    {
        var caster = new Npc { TemplateId = 1 };
        var target = new Npc { TemplateId = 9385 };
        var neighbor = new Npc { TemplateId = 9385 };
        var effect = new KillNpcWithoutCorpseEffect { NpcId = 9385 };

        var targets = effect.GetRemovalTargets(caster, target, [neighbor, neighbor, caster]).ToArray();

        await Assert.That(targets.Length).IsEqualTo(2);
        await Assert.That(targets.Contains(target)).IsTrue();
        await Assert.That(targets.Contains(neighbor)).IsTrue();
        await Assert.That(targets.Contains(caster)).IsFalse();
        await Assert.That(effect.GetRemovalTargets(target, target, [target]).Count()).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CreditDefeat_RecordedTeamTakesTaggingRights_PrefersTeamWithTaggerFallback(bool teamMissing)
    {
        var npc = new RetreatingNpc();
        var first = new Character(new UnitCustomModelParams()) { ObjId = 1 };
        var winner = new Character(new UnitCustomModelParams()) { ObjId = 2 };
        var nearby = new Character(new UnitCustomModelParams()) { ObjId = 3 };
        npc.CharacterTagging.AddTagger(first, 1);
        // Arrange the tagging snapshot left by a different party crossing the damage threshold.
        typeof(Tagging).GetField("_tagTeam", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(npc.CharacterTagging, 42u);
        var team = new Team { Id = 42 };
        team.Members[0] = new TeamMember(winner);
        team.Members[1] = new TeamMember(nearby);
        var teams = Mock.Of<ITeamManager>();
        teams.GetActiveTeam(42).Returns(teamMissing ? null : team);
        var quests = Mock.Of<IQuestManager>();

        KillNpcWithoutCorpseEffect.CreditDefeat(npc, quests.Object, teams.Object, false);

        if (teamMissing)
        {
            quests.DoOnMonsterHuntEvents(first, npc).WasCalled(Times.Once);
            quests.DoOnMonsterHuntEvents(winner, npc).WasNeverCalled();
        }
        else
        {
            quests.DoOnMonsterHuntEvents(first, npc).WasNeverCalled();
            quests.DoOnMonsterHuntEvents(winner, npc).WasCalled(Times.Once);
            quests.DoOnMonsterHuntEvents(nearby, npc).WasCalled(Times.Once);
        }
        await Task.CompletedTask;
    }

    [Test]
    public async Task CreditDefeat_RetreatAtHalfHealth_CreditsTaggerWithoutKillingNpc()
    {
        var npc = new RetreatingNpc { Hp = 50 };
        var player = new Character(new UnitCustomModelParams());
        npc.CharacterTagging.AddTagger(player, 50);
        var quests = Mock.Of<IQuestManager>();

        KillNpcWithoutCorpseEffect.CreditDefeat(npc, quests.Object, Mock.Of<ITeamManager>().Object, false);

        quests.DoOnMonsterHuntEvents(player, npc).WasCalled(Times.Once);
        await Assert.That(npc.Hp).IsEqualTo(50);
    }

    [Test]
    public async Task CreditDefeat_LympherodaRetreat_RaisesRequiredMonsterHuntEvent()
    {
        var npc = new RetreatingNpc { TemplateId = 9385, Hp = 50 };
        var player = new Character(new UnitCustomModelParams());
        npc.CharacterTagging.AddTagger(player, 50);
        var count = 0u;
        var npcId = 0u;
        player.Events.OnMonsterHunt += (_, args) =>
        {
            npcId = args.NpcId;
            count += args.Count;
        };
        var quests = new QuestManager(Mock.Of<ITaskManager>().Object, Mock.Of<IZoneManager>().Object);

        KillNpcWithoutCorpseEffect.CreditDefeat(npc, quests, Mock.Of<ITeamManager>().Object, false);

        await Assert.That(npcId).IsEqualTo(9385u);
        await Assert.That(count).IsEqualTo(1u);
        await Assert.That(npc.Hp).IsEqualTo(50);
    }

    [Test]
    public async Task CreditDefeat_UntaggedNpc_DoesNotAwardCredit()
    {
        var quests = Mock.Of<IQuestManager>();

        KillNpcWithoutCorpseEffect.CreditDefeat(new RetreatingNpc(), quests.Object,
            Mock.Of<ITeamManager>().Object, false);

        quests.DoOnMonsterHuntEvents(Any<ICharacter>(), Any<Npc>()).WasNeverCalled();
        await Task.CompletedTask;
    }

    [Test]
    public async Task CreditDefeat_PartyBelowTagThreshold_CreditsNearbyMembersOnce()
    {
        var npc = new RetreatingNpc();
        var player = new Character(new UnitCustomModelParams()) { ObjId = 1 };
        var nearby = new Character(new UnitCustomModelParams()) { ObjId = 2 };
        npc.CharacterTagging.AddTagger(player, 50);
        var team = new Team();
        team.Members[0] = new TeamMember(player);
        team.Members[1] = new TeamMember(nearby);
        var teams = Mock.Of<ITeamManager>();
        teams.GetTeamByObjId(player.ObjId).Returns(team);
        var quests = Mock.Of<IQuestManager>();

        KillNpcWithoutCorpseEffect.CreditDefeat(npc, quests.Object, teams.Object, false);

        quests.DoOnMonsterHuntEvents(player, npc).WasCalled(Times.Once);
        quests.DoOnMonsterHuntEvents(nearby, npc).WasCalled(Times.Once);
        await Assert.That(npc.CharacterTagging.TagTeam).IsEqualTo(0u);
    }

    private sealed class RetreatingNpc : Npc
    {
        public override int MaxHp { get; set; } = 100;
    }
}
