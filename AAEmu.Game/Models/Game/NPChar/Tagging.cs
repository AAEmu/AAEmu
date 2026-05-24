using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Items.Containers;

namespace AAEmu.Game.Models.Game.NPChar;

public class Tagging(Unit owner)
{
    private readonly object _lock = new();
    private Dictionary<Character, int> _taggers = [];
    private Character _tagger;
    private uint _tagTeam;
    // private int _totalDamage;

    public Unit Owner { get; } = owner;

    public Character Tagger
    {
        get
        {
            lock (_lock)
            {
                return _tagger;
            }
        }
    }

    public uint TagTeam
    {
        get
        {
            lock (_lock)
            {
                return _tagTeam;
            }
        }
    }

    public void ClearAllTaggers()
    {
        lock (_lock)
        {
            _taggers = [];
            _tagger = null;
            _tagTeam = 0;
            // _totalDamage = 0;
        }
    }

    public void AddTagger(Unit checkUnit, int damage)
    {
        lock (_lock)
        {
            // Check if the character is a pet, if so, propagate its user
            if (checkUnit is Units.Mate mate)
            {
                checkUnit = WorldManager.Instance.GetCharacterByObjId(mate.OwnerObjId) ?? checkUnit;
            }

            if (checkUnit is Character player)
            {
                if (_taggers.TryAdd(player, damage))
                {
                    _tagger ??= player;
                }
                else
                {
                    _taggers[player] += damage;
                }

                // _totalDamage += damage;

                // Check if the character is in a party
                if (player.InParty)
                {
                    var checkTeam = TeamManager.Instance.GetTeamByObjId(player.ObjId);
                    var partyDamage = 0;
                    foreach (var member in checkTeam.Members)
                    {
                        if (member == null || member.Character == null)
                            continue;

                        if (member.Character.GetDistanceTo(Owner, true) <= LootingContainer.MaxLootingRange)
                        {
                            // tm is an eligible party member
                            if (_taggers.TryGetValue(member.Character, out var value))
                            {
                                // Tagger is already in the list
                                partyDamage += value;
                            }
                        }
                    }
                    // Did the party do more than 50% of the total HP in damage?
                    if (partyDamage > Owner.MaxHp * 0.5)
                    {
                        _tagTeam = checkTeam.Id;
                    }
                }
                else
                {
                    if (_taggers[player] > Owner.MaxHp * 0.5)
                    {
                        _tagger = player;
                    }
                }
            }
            // TODO: packet to set red-but-not-aggro HP bar for taggers, "dull red" HP bar for not-taggers
        }
    }

    /// <summary>
    /// Returns every player who dealt damage to this NPC plus their party / raid
    /// mates that are within <paramref name="range"/>. Used by the TagShareEnabled
    /// feature in <c>Npc.DoDie</c> to fan out quest credit to all contributors —
    /// even ones who didn't pass the 50% HP threshold that <see cref="Tagger"/> uses.
    /// Loot and XP are NOT routed through this list; only quest events.
    /// </summary>
    public HashSet<Character> GetAllContributors(float range)
    {
        // Snapshot the damage-dealer keys under the tagging lock, then resolve teams
        // outside the lock to avoid holding the tagging lock while reaching into
        // TeamManager (which has its own lock domain).
        Character[] dealers;
        lock (_lock)
        {
            dealers = new Character[_taggers.Count];
            _taggers.Keys.CopyTo(dealers, 0);
        }

        var result = new HashSet<Character>();
        // Skip re-resolving the same team when multiple dealers are in the same party
        // or raid — GetTeamByObjId is an O(teams*members) scan, so on a busy mob
        // where several damage dealers share one raid we'd otherwise pay that cost
        // once per dealer.
        var visitedTeams = new HashSet<uint>();
        foreach (var dealer in dealers)
        {
            if (dealer == null || !dealer.IsOnline) continue;
            if (dealer.GetDistanceTo(Owner, true) > range) continue;

            result.Add(dealer);

            // Pull in party / raid mates within range
            if (!dealer.InParty) continue;
            var team = TeamManager.Instance.GetTeamByObjId(dealer.ObjId);
            if (team == null || !visitedTeams.Add(team.Id)) continue;

            foreach (var member in team.Members)
            {
                if (member?.Character == null || !member.Character.IsOnline) continue;
                if (member.Character.GetDistanceTo(Owner, true) > range) continue;
                result.Add(member.Character);
            }
        }
        return result;
    }
}
