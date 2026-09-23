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
        var opponent = corps == InstantCorps.Corps1 ? _corps2Result : _corps1Result;

        scorer.Score += score;

        BroadcastPacket(new SCInstantGameAddPointPacket(_zoneInstanceId, corps, score, result.Score,
            opponent.Score, scorer.CharacterName));

        EvaluateVictory();
    }

    /// <summary>
    /// A threshold from <c>game_rule_sets</c> was reached — declare the winner and finish. Only a
    /// match that is actually playing can be decided, and only once (the finish guard in
    /// <see cref="EndGame"/> plus the cancelled playing clock keep a second trigger out).
    /// </summary>
    private void EvaluateVictory()
    {
        if (Phase != InstantGamePhase.Playing)
            return;

        var ruleSet = _battlefield.RuleSet;

        var corps1Score = InstantGameResultRules.IsScoreVictory(_corps1Result.Score, ruleSet.VictoryScore);
        var corps2Score = InstantGameResultRules.IsScoreVictory(_corps2Result.Score, ruleSet.VictoryScore);
        if (corps1Score || corps2Score)
        {
            DeclareVictory(BattlefieldEndingReason.AchievementScore, corps1Score, corps2Score);
            return;
        }

        var corps1Kills = InstantGameResultRules.IsKillVictory(_corps1Result.TotalKills, ruleSet.VictoryKillCount);
        var corps2Kills = InstantGameResultRules.IsKillVictory(_corps2Result.TotalKills, ruleSet.VictoryKillCount);
        if (corps1Kills || corps2Kills)
            DeclareVictory(BattlefieldEndingReason.AchievementKillCount, corps1Kills, corps2Kills);
    }

    private void DeclareVictory(BattlefieldEndingReason reason, bool corps1Won, bool corps2Won)
    {
        _endingReason = reason;
        if (corps1Won && corps2Won)
        {
            // Both sides crossed the threshold on the same tally step; content has no double winner.
            _corps1Result.State = VictoryState.Draw;
            _corps2Result.State = VictoryState.Draw;
        }
        else
        {
            _corps1Result.State = corps1Won ? VictoryState.Win : VictoryState.Lose;
            _corps2Result.State = corps2Won ? VictoryState.Win : VictoryState.Lose;
        }

        // Stops the playing clock so the time-over path cannot overwrite the declared result.
        _endGameTokenSource.Cancel();
        _ = EndGame();
    }

    public void OnKill(object sender, OnKillArgs args)
    {
        if (Phase != InstantGamePhase.Playing)
            return;
        if (args.Killer is not Character killer || args.Victim is not Character victim)
            return;

        if (!_members.TryGetValue(killer, out var memberKiller) ||
            !_members.TryGetValue(victim, out var memberVictim) ||
            !_characterCorps.TryGetValue(killer, out var killerCorps) ||
            !_characterCorps.TryGetValue(victim, out var victimCorps))
            return;

        memberKiller.Kills++;
        memberKiller.Killstreak++;

        memberVictim.Deaths++;
        memberVictim.Killstreak = 0;

        BroadcastPacket(new SCInstantGameKillPacket(_zoneInstanceId, killer, victim, killerCorps, victimCorps, (sbyte)memberKiller.Killstreak, memberKiller.Corps.TotalKills, memberVictim.Corps.TotalKills));
        killer.SendPacket(new SCInstantGameKillstreakPacket(_zoneInstanceId, (sbyte)memberKiller.Killstreak, true));

        // Points come from this rule set's game_score_rules row for the final-hitter kill event.
        // event_value 0 is the plain-kill row every shipped battlefield rule set scores with; a
        // rule set without that row awards nothing and says so.
        if (!_battlefield.RuleSet.TryGetEventScore(GameScoreEventKind.KillEnemyUnitFinalHitter,
                eventValue: 0, corps: (int)killerCorps, eventTagId: 0, out var killScore))
        {
            _log.Warn(
                "[InstantGame] rule set {0} has no game_score_rules row for kill_enemy_unit_final_hitter; kill awards no score (battlefield {1})",
                _battlefield.RuleSet.Id, _battlefield.Id);
        }

        AddScore(killerCorps, memberKiller, killScore);

        var corps = victimCorps;
        var spawn = corps == InstantCorps.Corps1 ? _battlefield.Spawns.Corps1Spawn : _battlefield.Spawns.Corps2Spawn;

        // Respawn delay is the rule set's own time_resurrection_delay; the reset only runs while
        // the match is still being played (a finish cancels it with the rest of the clock).
        Task.Run(async () =>
        {
            await Delay(TimeSpan.FromSeconds(Math.Max(0, _battlefield.RuleSet.TimeResurrectionDelay)),
                _endGameTokenSource.Token);
            var stillHere = false;
            lock (_rosterLock)
                stillHere = Phase == InstantGamePhase.Playing && _members.ContainsKey(victim);
            if (!stillHere || victim.Level <= 0)
                return;

            // TODO: Prevent fall damage for both killer and victim on teleport
            victim.BroadcastPacket(new SCCharacterResurrectedPacket(victim.ObjId, spawn.X, spawn.Y, spawn.Z, spawn.RotationZ), true);
            victim.ResetAllSkillCooldowns(false);
            victim.Buffs.RemoveAllEffects();
            if (victim.Level > 0)
            {
                victim.Hp = victim.MaxHp;
                victim.Mp = victim.MaxMp;
                victim.BroadcastPacket(new SCUnitPointsPacket(victim.ObjId, victim.Hp, victim.Mp), true);
            }

            spawn = corps == InstantCorps.Corps1 ? _battlefield.Spawns.Corps2Spawn : _battlefield.Spawns.Corps1Spawn;

            if (_battlefield.Id == (uint)InstantGameType.Gladiator)
            {
                if (killer.Hp == 0)
                    killer.BroadcastPacket(new SCCharacterResurrectedPacket(killer.ObjId, spawn.X, spawn.Y, spawn.Z, spawn.RotationZ), true);
                else
                    killer.SendPacket(new SCTeleportUnitPacket(0, 0, spawn.X, spawn.Y, spawn.Z, spawn.RotationZ));

                if (killer.Level > 0)
                {
                    killer.Hp = killer.MaxHp;
                    killer.Mp = killer.MaxMp;
                    killer.BroadcastPacket(new SCUnitPointsPacket(killer.ObjId, killer.Hp, killer.Mp), true);
                }

                killer.Buffs.RemoveAllEffects();
                killer.ResetAllSkillCooldowns(false);
            }
        }, _endGameTokenSource.Token);

    }
}
