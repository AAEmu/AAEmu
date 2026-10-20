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
