using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Static;
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
    private bool _fist; // скилл 2 анимация 1 и 2
    private bool _rightHand; // скилл 2 анимация 3 и 87
    private bool _leftHand; // скилл 3 анимация 4 и 87
    private bool _twoHand;
    private bool _shield;
    private bool _bow; // по идее скилл 4

    private readonly Character _character;
    private readonly int _fireAnimId;
    private static readonly Dictionary<int, int> CharacterFireAnimDataRightHand = new()
    {
        { 3, 46 }, { 87, 35 }
    };
    private static readonly Dictionary<int, int> CharacterFireAnimDataLeftHand = new()
    {
        { 4, 45 }, { 88, 35 }
    };
    private static readonly Dictionary<int, int> CharacterFireAnimDataTwoHand = new()
    {
        { 7, 45 }, { 95, 45 }, { 139, 45 }
    };
    private static readonly Dictionary<int, int> CharacterFireAnimDataBow = new()
    {
        { 9, 45 }
    };
    private static readonly Dictionary<int, int> CharacterFireAnimDataFist = new()
    {
        { 1, 26 }, { 2, 80 }
    };
    private static readonly Dictionary<int, int> NpcFireAnimData = new()
    {
        { 1, 37 }, { 2, 80 }
    };

    private static Dictionary<int, int> FireAnimData;
    private static Queue<int> FireAnimQueue;
    private static Queue<int> NpcFireAnimQueue;

    public short ComputedDelay { get; set; }
    private ExtraDataFlags ExtraDataFlag { get; set; }
    private byte ExtraDataByte { get; set; }
    private ushort ExtraDataUShort { get; set; }
    private uint ExtraDataUInt { get; set; }
    private bool ExtraDataBool { get; set; }

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
        _fist = false;
        _rightHand = false;
        _leftHand = false;
        _shield = false;
        _twoHand = false;

        if (_skill.Template.Id == 2 || _skill.Template.Id == 3)
        {
            if (_character is not null)
            {
                var mainHandItem = _character.Equipment.GetItemBySlot(15);
                var offHandItem = _character.Equipment.GetItemBySlot(16);

                if (mainHandItem is not null)
                {
                    _rightHand = true;
                }
                if (offHandItem is not null)
                {
                    _leftHand = true;
                }
                if (_character.Buffs.CheckBuff((uint)BuffConstants.EquipShield))
                {
                    _shield = true;
                    _leftHand = false;
                }
                if (_character.Buffs.CheckBuff((uint)BuffConstants.EquipTwoHanded))
                {
                    _twoHand = true;
                    _rightHand = false;
                    _leftHand = false;
                }
                if (!_twoHand && !_rightHand && !_leftHand)
                {
                    _fist = true;
                }
            }

            _fireAnimId = GetNextAnimationId();
        }

        if ((_skill.Template.Id == 2 || _skill.Template.Id == 3) && _character is not null)
        {
            Logger.Info($"SkillFired: Id={_id}:{_fireAnimId}, caster={baseUnit.ObjId}, target={_target.ObjId}");
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

    private int GetNextAnimationId()
    {
        if (_character is not null)
        {
            if (FireAnimQueue == null || FireAnimQueue.Count == 0)
            {
                FireAnimData = new Dictionary<int, int>();
                var allKeys = Enumerable.Empty<int>();

                // только меч в правой руке
                // или меч в правой руке и щит в левой
                if (_rightHand || (_rightHand && _shield))
                {
                    allKeys = allKeys.Concat(CharacterFireAnimDataRightHand.Keys);
                    MergeDictionaries(FireAnimData, CharacterFireAnimDataRightHand);
                }
                // только меч в левой руке
                if (_rightHand && _leftHand)
                {
                    allKeys = allKeys.Concat(CharacterFireAnimDataRightHand.Keys);
                    MergeDictionaries(FireAnimData, CharacterFireAnimDataRightHand);
                    allKeys = allKeys.Concat(CharacterFireAnimDataLeftHand.Keys);
                    MergeDictionaries(FireAnimData, CharacterFireAnimDataLeftHand);
                }
                // только меч в левой руке
                if (!_rightHand && _leftHand)
                {
                    allKeys = allKeys.Concat(CharacterFireAnimDataFist.Keys);
                    MergeDictionaries(FireAnimData, CharacterFireAnimDataFist);
                    allKeys = allKeys.Concat(CharacterFireAnimDataLeftHand.Keys);
                    MergeDictionaries(FireAnimData, CharacterFireAnimDataLeftHand);
                }
                // только двуручный меч
                if (_twoHand)
                {
                    allKeys = allKeys.Concat(CharacterFireAnimDataTwoHand.Keys);
                    MergeDictionaries(FireAnimData, CharacterFireAnimDataTwoHand);
                }
                // только кулаки
                if (_fist)
                {
                    allKeys = CharacterFireAnimDataFist.Keys;
                    FireAnimData = CharacterFireAnimDataFist;
                }

                // Перемешивание ключей
                var rng = new Random();
                allKeys = allKeys.OrderBy(x => rng.Next());
                FireAnimQueue = new Queue<int>(allKeys);
            }
            // Dequeue the next animation ID
            return FireAnimQueue.Dequeue();
        }

        // если не персонаж, то Npc
        if (NpcFireAnimQueue == null || NpcFireAnimQueue.Count == 0)
        {
            var allKeys = NpcFireAnimData.Keys;
            NpcFireAnimQueue = new Queue<int>(allKeys);
        }
        // Dequeue the next animation ID
        return NpcFireAnimQueue.Dequeue();
    }

    private void MergeDictionaries(Dictionary<int, int> target, Dictionary<int, int> source)
    {
        foreach (var kvp in source)
        {
            target[kvp.Key] = kvp.Value;
        }
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

                //stream.Write((byte)1);  // f - When changed to 1 when firing an auto-casting skill, will make the little blue arrow.
                //stream.Write((byte)15); // c

                ExtraDataFlag = ExtraDataFlags.HasByte;
                SetSkillResult(SkillResult.TooFarRange); // ExtraDataByte = 15;
                WriteExtraData(stream);

                stream.WritePisc(_id, 0); // added skill type here in 3.0.3.0
                stream.Write((byte)0); // flag
            }
            else
            {
                // отдельные удары
                stream.Write((short)FireAnimData[_fireAnimId]); // выставляем время для ближнего боя в зависимости от анимации
                stream.Write((short)0);

                //stream.Write((byte)0); // f - When changed to 1 when firing an auto-casting skill, will make the little blue arrow.
                WriteExtraData(stream);

                stream.WritePisc(_id, _fireAnimId); // added skill type here in 3.0.3.0
                stream.Write((byte)0); // flag
            }
        }
        else
        {
            // дальняя атака
            stream.Write((short)(ComputedDelay / 10 + 10)); // TODO  +10 It became visible flying arrows 
            stream.Write((short)(_skill.Template.ChannelingTime / 10 + 10));

            //stream.Write((byte)0); // f - When changed to 1 when firing an auto-casting skill, will make the little blue arrow.
            WriteExtraData(stream);

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

            //stream.Write((byte)0); // f - When changed to 1 when firing an auto-casting skill, will make the little blue arrow.
            WriteExtraData(stream);

            stream.WritePisc(_id, _fireAnimId); // added skill type here in 3.0.3.0
            stream.Write((byte)0); // flag
        }
        else
        {
            // дальняя атака
            stream.Write((short)(ComputedDelay / 10 + 10)); // TODO  +10 It became visible flying arrows 
            stream.Write((short)(_skill.Template.ChannelingTime / 10 + 10));

            //stream.Write((byte)0); // f - When changed to 1 when firing an auto-casting skill, will make the little blue arrow.
            WriteExtraData(stream);

            stream.WritePisc(_id, _skill.Template.FireAnim?.Id ?? 0); // added skill type here in 3.0.3.0
            stream.Write((byte)0); // flag
        }
    }

    private void WriteExtraData(PacketStream stream)
    {
        stream.Write((byte)ExtraDataFlag); // f
        if (ExtraDataFlag.HasFlag(ExtraDataFlags.HasByte))
            stream.Write(ExtraDataByte);   // c
        if (ExtraDataFlag.HasFlag(ExtraDataFlags.HasUShort))
            stream.Write(ExtraDataUShort); // e
        if (ExtraDataFlag.HasFlag(ExtraDataFlags.HasUInt))
            stream.Write(ExtraDataUInt);   // p
        if (ExtraDataFlag.HasFlag(ExtraDataFlags.HasBool))
            stream.Write(ExtraDataBool);   // d
    }

    public SCSkillFiredPacket SetSkillResult(SkillResult skillResult)
    {
        if (skillResult != SkillResult.Success)
            ExtraDataFlag |= ExtraDataFlags.HasByte;
        else
            ExtraDataFlag &= ~ExtraDataFlags.HasByte;
        ExtraDataByte = (byte)skillResult;

        return this;
    }

    public SCSkillFiredPacket SetResultUShort(ushort val)
    {
        if (val != 0)
            ExtraDataFlag |= ExtraDataFlags.HasUShort;
        else
            ExtraDataFlag &= ~ExtraDataFlags.HasUShort;
        ExtraDataUShort = val;

        return this;
    }

    public SCSkillFiredPacket SetResultUInt(uint val)
    {
        if (val != 0)
            ExtraDataFlag |= ExtraDataFlags.HasUInt;
        else
            ExtraDataFlag &= ~ExtraDataFlags.HasUInt;
        ExtraDataUInt = val;

        return this;
    }

    public SCSkillFiredPacket SetResultBool(bool val)
    {
        if (val != false)
            ExtraDataFlag |= ExtraDataFlags.HasBool;
        else
            ExtraDataFlag &= ~ExtraDataFlags.HasBool;
        ExtraDataBool = val;

        return this;
    }

    public override string Verbose()
    {
        return $" - Id {_id}, TlId {_tl}, Caster {_caster.ObjId}, Target {_target.ObjId}, Skill {_skill.Template.Id}";
    }
}
