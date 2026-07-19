using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.InstantGame.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.InstantGame;

public partial class InstantGame
{
    private void AddScore(InstantCorps corps, InstantGameTeamMember scorer, int score)
    {
        var result = corps == InstantCorps.Corps1 ? _corps1Result : _corps2Result;

        scorer.Score += score;

        BroadcastPacket(new SCInstantGameAddPointPacket(_zoneInstanceId, InstantCorps.Corps1, score, result.Score, 0, scorer.Character.Name));

        if (result.Score >= _battlefield.RuleSet.VictoryScore)
        {
            result.State = VictoryState.Win;
            _endGameTokenSource.Cancel();
            EndGame().Start();
        }
    }

    public void OnKill(object sender, OnKillArgs args)
    {
        if (args.Killer is not Character killer || args.Victim is not Character victim)
            return;

        // _log.Debug("{0} killed {1}", args.Killer.Name, args.Victim.Name, character.Name);

        var memberKiller = _members[killer];
        memberKiller.Kills++;
        memberKiller.Killstreak++;

        var memberVictim = _members[victim];
        memberVictim.Deaths++;
        memberVictim.Killstreak = 0;

        BroadcastPacket(new SCInstantGameKillPacket(_zoneInstanceId, killer, victim, _characterCorps[killer], _characterCorps[victim], (sbyte)memberKiller.Killstreak, memberKiller.Corps.TotalKills, memberVictim.Corps.TotalKills));
        killer.SendPacket(new SCInstantGameKillstreakPacket(_zoneInstanceId, (sbyte)memberKiller.Killstreak, 0, true));
        // TODO: Get score from events
        AddScore(_characterCorps[killer], memberKiller, 30);

        var corps = _characterCorps[victim];
        var spawn = corps == InstantCorps.Corps1 ? _battlefield.Spawns.Corps1Spawn : _battlefield.Spawns.Corps2Spawn;

        Task.Run(async () =>
        {
            await Task.Delay(6000);
            // Reset victim
            // TODO: Prevent fall damage for both killer and victim on teleport
            victim.BroadcastPacket(new SCCharacterResurrectedPacket(victim.ObjId, spawn.X, spawn.Y, spawn.Z, spawn.RotationZ), true); // Ressurrect
            victim.ResetAllSkillCooldowns(false); // Skill Cooldown Reset
            victim.Buffs.RemoveAllEffects(); // Buff Reset

            victim.Hp = victim.MaxHp; // Full HP
            victim.Mp = victim.MaxMp; // Full MP
            victim.BroadcastPacket(new SCUnitPointsPacket(victim.ObjId, victim.Hp, victim.Mp, victim.HighAbilityRsc), true); // Reset HP and MP

            spawn = corps == InstantCorps.Corps1 ? _battlefield.Spawns.Corps2Spawn : _battlefield.Spawns.Corps1Spawn;

            // Reset killer
            if (_battlefield.Id == (uint)InstantGameType.Gladiator)
            {
                if (killer.Hp == 0)
                {
                    // Killer somehow died
                    killer.BroadcastPacket(new SCCharacterResurrectedPacket(killer.ObjId, spawn.X, spawn.Y, spawn.Z, spawn.RotationZ), true);
                }
                else
                    killer.SendPacket(new SCTeleportUnitPacket(0, 0, spawn.X, spawn.Y, spawn.Z, spawn.RotationZ));

                killer.Hp = killer.MaxHp;
                killer.Mp = killer.MaxMp;
                killer.BroadcastPacket(new SCUnitPointsPacket(killer.ObjId, killer.Hp, killer.Mp, killer.HighAbilityRsc), true);
                killer.Buffs.RemoveAllEffects();
                killer.ResetAllSkillCooldowns(false);
            }
        });

    }
}
