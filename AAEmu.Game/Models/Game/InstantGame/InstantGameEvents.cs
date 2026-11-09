using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.InstantGame.Static;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models;

namespace AAEmu.Game.Models.Game.InstantGame;

public partial class InstantGame
{
    private bool AddScore(InstantCorps corps, InstantGameTeamMember scorer, int score)
    {
        var result = corps == InstantCorps.Corps1 ? _corps1Result : _corps2Result;

        scorer.Score += score;

        BroadcastPacket(new SCInstantGameAddPointPacket(_zoneInstanceId, corps, score, result.Score, 0, scorer.Character.Name));

        if (result.Score >= _battlefield.RuleSet.VictoryScore)
        {
            result.State = VictoryState.Win;
            _endGameTokenSource.Cancel();
            _ = EndGame();
            return true; // Match is over!
        }

        return false; // Match continues to next round
    }

    public void OnKill(object sender, OnKillArgs args)
    {
        if (args.Killer is not Character killer || args.Victim is not Character victim)
            return;

        var memberKiller = _members[killer];
        memberKiller.Kills++;
        memberKiller.Killstreak++;

        var memberVictim = _members[victim];
        memberVictim.Deaths++;
        memberVictim.Killstreak = 0;

        BroadcastPacket(new SCInstantGameKillPacket(_zoneInstanceId, killer, victim, _characterCorps[killer], _characterCorps[victim], (sbyte)memberKiller.Killstreak, memberKiller.Corps.TotalKills, memberVictim.Corps.TotalKills));
        killer.SendPacket(new SCInstantGameKillstreakPacket(_zoneInstanceId, (sbyte)memberKiller.Killstreak, 0, true));

        //To Do: assist track system and reward the assist points from world.json in configuration(already implemented).

        //get kill points reward from world.json in configuration folder
        var isMatchOver = AddScore(_characterCorps[killer], memberKiller, AppConfiguration.Instance.World.GladiatorKillScore);
        //old direct point reward var isMatchOver = AddScore(_characterCorps[killer], memberKiller, 30);

        // If Round 2 (or final score) ended the match, reset players to platforms so they stand up for the scoreboard!
        if (isMatchOver)
        {
            var endCorps = _characterCorps[victim];
            var endSpawn = endCorps == InstantCorps.Corps1 ? _battlefield.Spawns.Corps1Spawn : _battlefield.Spawns.Corps2Spawn;
            var endKillerSpawn = endCorps == InstantCorps.Corps1 ? _battlefield.Spawns.Corps2Spawn : _battlefield.Spawns.Corps1Spawn;

            Task.Run(async () =>
            {
                await Task.Delay(3000);

                // 1. Reset Victim to platform and resurrect
                victim.Transform.ApplyWorldSpawnPosition(
                    new AAEmu.Game.Models.Game.World.Transform.WorldSpawnPosition
                    {
                        ZoneId = _battlefield.ZoneKey,
                        X = endSpawn.X,
                        Y = endSpawn.Y,
                        Z = endSpawn.Z
                    },
                    _world.Id
                );
                victim.BroadcastPacket(new SCCharacterResurrectedPacket(victim.ObjId, endSpawn.X, endSpawn.Y, endSpawn.Z, endSpawn.RotationZ), true);
                victim.Hp = victim.MaxHp;
                victim.Mp = victim.MaxMp;
                victim.BroadcastPacket(new SCUnitPointsPacket(victim.ObjId, victim.Hp, victim.Mp), true);
                victim.Buffs.RemoveAllEffects();
                victim.ResetAllSkillCooldowns(false);

                // 2. Reset Killer to platform
                if (_battlefield.Id == (uint)InstantGameType.Gladiator)
                {
                    killer.Transform.ApplyWorldSpawnPosition(
                        new AAEmu.Game.Models.Game.World.Transform.WorldSpawnPosition
                        {
                            ZoneId = _battlefield.ZoneKey,
                            X = endKillerSpawn.X,
                            Y = endKillerSpawn.Y,
                            Z = endKillerSpawn.Z
                        },
                        _world.Id
                    );
                    killer.SendPacket(new SCTeleportUnitPacket(0, 0, endKillerSpawn.X, endKillerSpawn.Y, endKillerSpawn.Z, endKillerSpawn.RotationZ));
                    killer.Hp = killer.MaxHp;
                    killer.Mp = killer.MaxMp;
                    killer.BroadcastPacket(new SCUnitPointsPacket(killer.ObjId, killer.Hp, killer.Mp), true);
                    killer.Buffs.RemoveAllEffects();
                    killer.ResetAllSkillCooldowns(false);
                }
            });
            return;
        }

        var corps = _characterCorps[victim];
        var spawn = corps == InstantCorps.Corps1 ? _battlefield.Spawns.Corps1Spawn : _battlefield.Spawns.Corps2Spawn;

        //normal round start
        Task.Run(async () =>
        {
            await Task.Delay(6000);

            // Sync victim server position before resurrecting for fall damage prevention
            victim.Transform.ApplyWorldSpawnPosition(
                new AAEmu.Game.Models.Game.World.Transform.WorldSpawnPosition
                {
                    ZoneId = _battlefield.ZoneKey,
                    X = spawn.X,
                    Y = spawn.Y,
                    Z = spawn.Z
                },
                _world.Id
            );

            // Reset victim for the next round
            victim.BroadcastPacket(new SCCharacterResurrectedPacket(victim.ObjId, spawn.X, spawn.Y, spawn.Z, spawn.RotationZ), true); // Resurrect
            victim.ResetAllSkillCooldowns(false); // Skill Cooldown Reset
            victim.Buffs.RemoveAllEffects(); // Buff Reset

            victim.Hp = victim.MaxHp; // Full HP
            victim.Mp = victim.MaxMp; // Full MP
            victim.BroadcastPacket(new SCUnitPointsPacket(victim.ObjId, victim.Hp, victim.Mp), true); // Reset HP and MP

            spawn = corps == InstantCorps.Corps1 ? _battlefield.Spawns.Corps2Spawn : _battlefield.Spawns.Corps1Spawn;

            // Reset killer for the next round
            if (_battlefield.Id == (uint)InstantGameType.Gladiator)
            {
                // Sync killer server position before teleporting
                killer.Transform.ApplyWorldSpawnPosition(
                    new AAEmu.Game.Models.Game.World.Transform.WorldSpawnPosition
                    {
                        ZoneId = _battlefield.ZoneKey,
                        X = spawn.X,
                        Y = spawn.Y,
                        Z = spawn.Z
                    },
                    _world.Id
                );

                if (killer.Hp == 0)
                {
                    // Killer somehow died
                    killer.BroadcastPacket(new SCCharacterResurrectedPacket(killer.ObjId, spawn.X, spawn.Y, spawn.Z, spawn.RotationZ), true);
                }
                else
                    killer.SendPacket(new SCTeleportUnitPacket(0, 0, spawn.X, spawn.Y, spawn.Z, spawn.RotationZ));

                killer.Hp = killer.MaxHp;
                killer.Mp = killer.MaxMp;
                killer.BroadcastPacket(new SCUnitPointsPacket(killer.ObjId, killer.Hp, killer.Mp), true);
                killer.Buffs.RemoveAllEffects();
                killer.ResetAllSkillCooldowns(false);
            }

            // Heal any landing fall damage 1.5 seconds after teleporting to the platform
            await Task.Delay(1500);
            victim.Hp = victim.MaxHp;
            victim.Mp = victim.MaxMp;
            victim.BroadcastPacket(new SCUnitPointsPacket(victim.ObjId, victim.Hp, victim.Mp), true);

            if (_battlefield.Id == (uint)InstantGameType.Gladiator)
            {
                killer.Hp = killer.MaxHp;
                killer.Mp = killer.MaxMp;
                killer.BroadcastPacket(new SCUnitPointsPacket(killer.ObjId, killer.Hp, killer.Mp), true);
            }
        });
    }
}
