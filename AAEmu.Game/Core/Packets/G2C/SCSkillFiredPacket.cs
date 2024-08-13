using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSkillFiredPacket : GamePacket
{
    public override PacketLogLevel LogLevel => PacketLogLevel.Trace;

    private readonly uint _id;
    private readonly ushort _tl;
    private readonly SkillCaster _caster;
    private readonly SkillCastTarget _target;
    private readonly SkillObject _skillObject;
    private readonly Skill _skill;
    private short _effectDelay = 37;
    private bool _dist;
    private bool _fist;
    private bool _oneHand;
    private bool _twoHand;
    private bool _bow;

    private readonly Character _character;
    private readonly int _fireAnimId;
    private static readonly Dictionary<int, int> CharacterFireAnimDataOneHand = new()
    {
        { 3, 45 }, { 4, 45 }, { 5, 45 }, { 6, 45 },
        { 87, 45 }, { 88, 45 }, { 89, 45 }, { 90, 45 },
        { 92, 45 }, { 93, 45 }, { 94, 45 }
    };
    private static readonly Dictionary<int, int> CharacterFireAnimDataTwoHand = new()
    {
        { 7, 45 }, { 95, 45 }, { 139, 45 }
    };
    private static readonly Dictionary<int, int> CharacterFireAnimDataBow = new()
    {
        { 9, 45 }
    };
    private static readonly Dictionary<int, int> NpcFireAnimData = new() { { 1, 50 }, { 2, 45 } };

    private static Dictionary<int, int> FireAnimData;
    private static Queue<int> FireAnimQueue;
    private static Queue<int> NpcFireAnimQueue;

    public short ComputedDelay { get; set; }

    public SCSkillFiredPacket(uint id, ushort tl, SkillCaster caster, SkillCastTarget target, Skill skill, SkillObject skillObject, BaseUnit baseUnit)
        : base(SCOffsets.SCSkillFiredPacket, 5)
    {
        _id = id;
        _tl = tl;
        _caster = caster;
        _target = target;
        _skill = skill;
        _skillObject = skillObject;
        _character = baseUnit as Character;

        if (_skill.Template.Id == 2)
        {
            if (_character is not null)
            {
                var mainHandItem = _character.Equipment.GetItemBySlot(15);
                var offHandItem = _character.Equipment.GetItemBySlot(16);

                _oneHand = mainHandItem != null;
                _fist = mainHandItem == null;
                _twoHand = offHandItem != null;
            }
            else
            {
                _oneHand = false;
                _twoHand = false;
                _fist = false;
            }

            _fireAnimId = GetNextAnimationId();
        }

        if (_skill.Template.Id == 2 && _character is not null)
        {
            Logger.Info($"SkillFired: Id={_id}:{_fireAnimId}, caster={baseUnit.ObjId}, target={_target.ObjId}");
        }
    }

    private int GetNextAnimationId()
    {
        if (_character is not null)
        {
            if (FireAnimQueue == null || FireAnimQueue.Count == 0)
            {
                FireAnimData = new Dictionary<int, int>();
                var allKeys = Enumerable.Empty<int>();

                if (_oneHand)
                {
                    allKeys = allKeys.Concat(CharacterFireAnimDataOneHand.Keys);
                    MergeDictionaries(FireAnimData, CharacterFireAnimDataOneHand);
                }

                if (_twoHand)
                {
                    allKeys = allKeys.Concat(CharacterFireAnimDataTwoHand.Keys);
                    MergeDictionaries(FireAnimData, CharacterFireAnimDataTwoHand);
                }

                if (_fist)
                {
                    allKeys = NpcFireAnimData.Keys;
                    FireAnimData = NpcFireAnimData;
                }

                // Перемешивание ключей
                var rng = new Random();
                allKeys = allKeys.OrderBy(x => rng.Next());
                FireAnimQueue = new Queue<int>(allKeys);
            }
            // Dequeue the next animation ID
            return FireAnimQueue.Dequeue();
        }
        else
        {
            // если не персонаж, то Npc
            if (NpcFireAnimQueue == null || NpcFireAnimQueue.Count == 0)
            {
                var allKeys = NpcFireAnimData.Keys;
                NpcFireAnimQueue = new Queue<int>(allKeys);
            }
            // Dequeue the next animation ID
            return NpcFireAnimQueue.Dequeue();
        }
    }

    private void MergeDictionaries(Dictionary<int, int> target, Dictionary<int, int> source)
    {
        foreach (var kvp in source)
        {
            target[kvp.Key] = kvp.Value;
        }
    }

    public SCSkillFiredPacket(uint id, ushort tl, SkillCaster caster, SkillCastTarget target, Skill skill, SkillObject skillObject)
        : base(SCOffsets.SCSkillFiredPacket, 5)
    {
        _id = id;
        _tl = tl;
        _caster = caster;
        _target = target;
        _skill = skill;
        _skillObject = skillObject;
    }

    public SCSkillFiredPacket(uint id, ushort tl, SkillCaster caster, SkillCastTarget target, Skill skill, SkillObject skillObject, short effectDelay = 37, int fireAnimId = 2, bool dist = true)
        : base(SCOffsets.SCSkillFiredPacket, 5)
    {
        _id = id;
        _tl = tl;
        _caster = caster;
        _target = target;
        _skill = skill;
        _skillObject = skillObject;
        _effectDelay = effectDelay;
        _fireAnimId = fireAnimId;
        _dist = dist;
    }

    public override PacketStream Write(PacketStream stream)
    {
        //stream.Write(_id);      // st - skill type  removed in 3.0.3.0
        stream.Write(_tl);       // sid - skill id

        stream.Write(_caster);      // SkillCaster
        stream.Write(_target);      // SkillCastTarget
        stream.Write(_skillObject); // SkillObject

        if (_character is not null)
        {
            WriteCharacterSkillData(stream);
        }
        else
        {
            WriteNpcSkillData(stream);
        }

        return stream;
    }

    private void WriteCharacterSkillData(PacketStream stream)
    {
        if (_skill.Template.Id == 2)
        {
            // ближняя атака
            if (_character?.IsAutoAttack == true)
            {
                // автоатака
                stream.Write((short)0);
                stream.Write((short)0);
                stream.Write((byte)1);  // f - When changed to 1 when firing an auto-casting skill, will make the little blue arrow.
                stream.Write((byte)15); // c
                stream.WritePisc(_id, 0); // added skill type here in 3.0.3.0
                stream.Write((byte)0); // flag
            }
            else
            {
                // отдельные удары
                stream.Write((short)FireAnimData[_fireAnimId]); // выставляем время для ближнего боя в зависимости от анимации
                stream.Write((short)0);
                stream.Write((byte)0); // f - When changed to 1 when firing an auto-casting skill, will make the little blue arrow.
                stream.WritePisc(_id, _fireAnimId); // added skill type here in 3.0.3.0
                stream.Write((byte)0); // flag
            }
        }
        else
        {
            // дальняя атака
            stream.Write((short)(ComputedDelay / 10 + 10)); // TODO  +10 It became visible flying arrows 
            stream.Write((short)(_skill.Template.ChannelingTime / 10 + 10));
            stream.Write((byte)0); // f - When changed to 1 when firing an auto-casting skill, will make the little blue arrow.
            stream.WritePisc(_id, _skill.Template.FireAnim?.Id ?? 0); // added skill type here in 3.0.3.0
            stream.Write((byte)0); // flag
        }
    }

    private void WriteNpcSkillData(PacketStream stream)
    {
        if (_skill.Template.Id == 2)
        {
            // ближняя атака
            stream.Write((short)NpcFireAnimData[_fireAnimId]); // выставляем время для ближнего боя в зависимости от анимации
            stream.Write((short)0);
            stream.Write((byte)0); // f - When changed to 1 when firing an auto-casting skill, will make the little blue arrow.
            stream.WritePisc(_id, _fireAnimId); // added skill type here in 3.0.3.0
            stream.Write((byte)0); // flag
        }
        else
        {
            // дальняя атака
            stream.Write((short)(ComputedDelay / 10 + 10)); // TODO  +10 It became visible flying arrows 
            stream.Write((short)(_skill.Template.ChannelingTime / 10 + 10));
            stream.Write((byte)0); // f - When changed to 1 when firing an auto-casting skill, will make the little blue arrow.
            stream.WritePisc(_id, _skill.Template.FireAnim?.Id ?? 0); // added skill type here in 3.0.3.0
            stream.Write((byte)0); // flag
        }
    }
}
