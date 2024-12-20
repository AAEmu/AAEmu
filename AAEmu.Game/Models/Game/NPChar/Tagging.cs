using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using System.Collections.Generic;
using AAEmu.Game.Models.Game.Items.Containers;

namespace AAEmu.Game.Models.Game.NPChar;

public class Tagging
{
    private object _lock = new();
    private Dictionary<Character, int> _taggers = [];
    private Character _tagger;
    private uint _tagTeam;
    // private int _totalDamage;

    public Unit Owner { get; }

    public Tagging(Unit owner)
    {
        Owner = owner;
    }

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
        _taggers = [];
        _tagger = null;
        _tagTeam = 0;
        // _totalDamage = 0;
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
}
