using System;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Shipyard;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

// 10.0.2.13 SC_PACKET_UNIT_STATE (opcode 151). Field order/types follow the binary UnitState_Serialize
// (x2game-dev_dedicate 0x393932E0) — see research/unit_state_struct.md. FIRST-PASS v10 structure: the
// spawn-essentials for a Character are byte-correct; values still TODO (worldId/regionId, the stat-group
// pish/pisc arrays, faction, flag bits, non-character id-block quant fields) are written with correct sizes
// but placeholder values, and `flags` = 0 so the client skips the optional firstHitter/highAbility/gmmode
// blocks. Needs live-client validation before being considered complete.
public class SCUnitStatePacket : GamePacket
{
    private readonly Unit _unit;
    private readonly BaseUnitType _baseUnitType;
#pragma warning disable IDE0052 // Remove unread private members
    // ReSharper disable once NotAccessedField.Local
    private ModelPostureType _modelPostureType;
#pragma warning restore IDE0052 // Remove unread private members

    public SCUnitStatePacket(Unit unit) : base(SCOffsets.SCUnitStatePacket, 1)
    {
        _unit = unit;
        _modelPostureType = unit.ModelPostureType;
        switch (_unit)
        {
            case Character _:
                _baseUnitType = BaseUnitType.Character;
                _modelPostureType = ModelPostureType.None;
                break;
            case Npc npc:
                _baseUnitType = BaseUnitType.Npc;
                _modelPostureType = npc.AnimActionId > 0 ? ModelPostureType.ActorModelState : ModelPostureType.None;
                break;
            case Slave _:
                _baseUnitType = BaseUnitType.Slave;
                _modelPostureType = ModelPostureType.TurretState;
                break;
            case House _:
                _baseUnitType = BaseUnitType.Housing;
                _modelPostureType = ModelPostureType.HouseState;
                break;
            case Transfer _:
                _baseUnitType = BaseUnitType.Transfer;
                _modelPostureType = ModelPostureType.TurretState;
                break;
            case Mate _:
                _baseUnitType = BaseUnitType.Mate;
                _modelPostureType = ModelPostureType.None;
                break;
            case Shipyard _:
                _baseUnitType = BaseUnitType.Shipyard;
                _modelPostureType = ModelPostureType.None;
                break;
        }
    }

    public override PacketStream Write(PacketStream stream)
    {
        var character = _unit as Character;
        var npc = _unit as Npc;

        // 1. ObjId (Bc, quantized-3)
        stream.WriteBc(_unit.ObjId);
        // 2. name
        stream.Write(_unit.Name);
        // 3-5. world placement (TODO: real instance/region ids)
        stream.Write((byte)0xFF);   // worldId  (capture: reference always sends 0xFF = derive region from position)
        stream.Write((byte)0xFF);   // regionId
        stream.Write(false);        // isInGlobalWorld

        // 6. id/type block: baseUnitType u8 + per-type ids (UnitState_SerializeIdTypeBlock)
        stream.Write((byte)_baseUnitType);
        switch (_baseUnitType)
        {
            case BaseUnitType.Character:
                stream.Write((long)(character?.Id ?? 0)); // charId (i64)
                stream.Write(0UL);                        // v
                break;
            case BaseUnitType.Npc:
                stream.WriteBc(npc!.ObjId);               // objId (Bc)
                stream.Write(npc.TemplateId);             // templateId (u32)
                stream.Write((long)npc.OwnerId);          // ownerId (i64)
                stream.Write((byte)0);                    // flag/clientDriven
                break;
            case BaseUnitType.Slave:
                var slave = (Slave)_unit;
                stream.Write((long)slave.Id);             // i64
                stream.Write(slave.TlId);                 // tl (TODO: binary single-quant)
                stream.Write(slave.TemplateId);           // templateId (u32)
                stream.Write((long)(slave.Summoner?.Id ?? 0)); // ownerId (i64)
                stream.Write((byte)0);                    // masterWorldId
                break;
            case BaseUnitType.Housing:
                var house = (House)_unit;
                var buildStep = house.CurrentStep == -1 ? 0 : -house.Template.BuildSteps.Count + house.CurrentStep;
                stream.Write(house.TlId);                 // tl (TODO: single-quant)
                stream.Write(house.TemplateId);           // templateId (u32)
                stream.Write(buildStep);                  // buildstep (i32)
                break;
            case BaseUnitType.Transfer:
                var transfer = (Transfer)_unit;
                stream.Write(transfer.TlId);              // tl (TODO: single-quant)
                stream.Write(transfer.TemplateId);        // templateId (u32)
                break;
            case BaseUnitType.Mate:
                var mount = (Mate)_unit;
                stream.Write((long)mount.Id);             // i64
                stream.Write(mount.TemplateId);           // templateId (u32)
                break;
            case BaseUnitType.Shipyard:
                var shipyard = (Shipyard)_unit;
                stream.Write((long)shipyard.ShipyardData.Id);   // i64
                stream.Write(shipyard.ShipyardData.TemplateId); // u32
                break;
        }

        // 7. master (owner name)
        stream.Write(_unit.OwnerId > 0 ? (NameManager.Instance.GetCharacterName(_unit.OwnerId) ?? "") : "");

        if (npc is not null)
        {
            var referenceHeight = WorldManager.Instance.GetReferenceHeight(npc.Ai, _unit.Transform.Local.Position.X, _unit.Transform.Local.Position.Y, _unit.Transform.Local.Position.Z, _unit.Transform.ZoneId);
            _unit.Transform.Local.SetHeight(referenceHeight);
        }

        // 8. position (wpos, 11-byte v10)
        stream.WritePosition(_unit.Transform.Local.Position);
        // 9. scale
        stream.Write(_unit.Scale);
        // 10-11. level, heirLevel
        stream.Write(_unit.Level);
        stream.Write((byte)0); // heirLevel (TODO)
        // 12. second level/heirLevel pair (UnitState_SerializeLevelBlock 0x39D1C060, obj +1270)
        stream.Write(_unit.Level);
        stream.Write((byte)0); // heirLevel (TODO)
        // 13. slot array (UnitState_SerializeSlotArray4 0x39D1C130, obj +9924): 4 × u8. TODO(v10): real values
        stream.Write((byte)0);
        stream.Write((byte)0);
        stream.Write((byte)0);
        stream.Write((byte)0);
        // 14. modelRef
        stream.Write(_unit.ModelId);
        // 15. equipment
        EquipmentSerializer.Write(stream, _unit, _baseUnitType);
        // 16. appearance — T1 of the v10 block carries the unit's race/gender; populate from the character
        if (character is not null)
        {
            _unit.ModelParams.Race = (byte)character.Race;
            _unit.ModelParams.Gender = (byte)character.Gender;
            _unit.ModelParams.VisualRace = (byte)character.Race;
            _unit.ModelParams.VisualGender = (byte)character.Gender;
        }
        stream.Write(_unit.ModelParams);
        // 17. target position (Bc; 0 = none)
        stream.WriteBc(0);
        // 18-19. precise hp/mp (i64 in v10)
        stream.Write((long)_unit.Hp * 100);
        stream.Write((long)_unit.Mp * 100);

        // 20-22. attach / bonding / interaction. Minimal "none/default" form for a standing unit:
        // attach point 0xFF = none (no trailing Bc), bonding 0xFF = none, interaction type 0 + isLooted false.
        stream.Write((byte)0xFF); // attach point (TODO: mounted units write point + Bc owner)
        stream.Write((byte)0xFF); // bonding point (TODO: bonded units write point + Bc + space/spot/type)
        stream.Write((byte)0);    // interaction type (default)
        stream.Write(false);      // isLooted

        // 23. activeWeapon
        stream.Write(_unit.ActiveWeapon);

        // 24-25. learned-skill / passive-buff counts (u8 each)
        var skillIds = character?.Skills.Skills.Values.Select(s => s.Id).ToArray() ?? Array.Empty<uint>();
        var passiveIds = character?.Skills.PassiveBuffs.Values.Select(b => b.Id).ToArray() ?? Array.Empty<uint>();
        stream.Write((byte)skillIds.Length);
        stream.Write((byte)passiveIds.Length);

        // 26-29. plain fields
        stream.Write(0u);  // type (TODO)
        stream.Write(character is not null ? character.Appellations.ActiveAppellation : 0u); // appellationStampId
        stream.Write(0u);  // vehicleDyeing (TODO)
        stream.Write(false); // isTempFaction

        // 30-31. learned skills + passive buffs — IDs packed via pish/pisc
        stream.WritePisc(skillIds);
        stream.WritePisc(passiveIds);

        // 32. heading — building/housing (idType 3) writes a single f32 yaw (radians); every other unit
        // writes 3 signed bytes (rot.x/y/z). Binary UnitState_Serialize idType==3 branch vs sub_3938AFC0.
        if (_baseUnitType == BaseUnitType.Housing)
        {
            stream.Write(_unit.Transform.Local.Rotation.Z); // yaw (f32, radians)
        }
        else
        {
            var (roll, pitch, yaw) = _unit.Transform.Local.ToRollPitchYawSBytes();
            stream.Write(roll);
            stream.Write(pitch);
            stream.Write(yaw);
        }

        // 33. raceGender (u8 = race | gender<<4)
        stream.Write(character?.RaceGender ?? npc?.RaceGender ?? _unit.RaceGender);

        // 34. stat groups — three pish/pisc groups of 4/3/4 values (TODO: map to real unit stats)
        stream.WritePisc(new uint[4]);
        stream.WritePisc(new uint[3]);
        stream.WritePisc(new uint[4]);

        // 35-36. flags (u16) + attckFactionFlags (u8). flags=0 -> client skips firstHitter/highAbility/gmmode.
        stream.Write((ushort)0);
        stream.Write((byte)0);

        // 38. character-only trailing block
        if (character is not null)
        {
            // expertise: 29 × { exp u32, order u8 } + nActive u8 + active u8 × n
            var abilities = character.Abilities.Values.ToList();
            foreach (var ability in abilities)
            {
                stream.Write(ability.Exp);
                stream.Write(ability.Order);
            }
            // pad to 29 entries if the model holds fewer (the binary always serializes 29 fixed slots)
            for (var i = abilities.Count; i < 29; i++)
            {
                stream.Write(0u);          // exp
                stream.Write((byte)0);     // order
            }
            var activeAbilities = character.Abilities.GetActiveAbilities();
            stream.Write((byte)activeAbilities.Count);
            foreach (var ability in activeAbilities)
                stream.Write((byte)ability);

            stream.WriteBc(0);             // 43. faction bc (3 raw bytes, obj +9880). TODO(v10): real faction
            stream.Write((byte)0xFF);      // duelTeamType (TODO)
            stream.Write((byte)0);         // camp (TODO)
            character.VisualOptions.WriteOptions(stream); // premium/cosplay visual block
            stream.Write(1);               // premium
            stream.Write(0);               // _pageInfos Size = 0 (empty)
            stream.Write(0);               // _selectPageIndex
            stream.Write(0);               // _extendMaxStats
            stream.Write(0);               // _applyExtendCount
            // 52. equipSlotReinforces — no presence byte on the wire (the serializer's optional groups are
            // always present), so both lists are written with a u32 Size. Empty for now.
            // slotInfoList 0x39390BA0 = u32 Size + Size×{key u32, level u8, exp u32};
            // levelEffectList 0x393916B0 = u32 Size + Size×{equipSlot u8, level u8, type u32}.
            stream.Write(0u);              // slotInfoList Size = 0
            stream.Write(0u);              // levelEffectList Size = 0
        }

        // Buff section (good / bad / hidden), each 88-byte record via WriteBuff
        var goodBuffs = new List<Buff>();
        var badBuffs = new List<Buff>();
        var hiddenBuffs = new List<Buff>();
        _unit.Buffs.GetAllBuffs(goodBuffs, badBuffs, hiddenBuffs, false);

        stream.Write((byte)Math.Min(goodBuffs.Count, 32));
        foreach (var effect in goodBuffs.Take(32))
            WriteBuff(stream, effect);

        stream.Write((byte)Math.Min(badBuffs.Count, 20));
        foreach (var effect in badBuffs.Take(20))
            WriteBuff(stream, effect);

        stream.Write((byte)Math.Min(hiddenBuffs.Count, 28));
        foreach (var effect in hiddenBuffs.Take(28))
            WriteBuff(stream, effect);

        return stream;
    }


    #region NetBuff
    // 10.0.2.13 buff record (UnitState_SerializeBuff 0x3938FA00): index group{u32} + SkillCaster +
    // buffId i64 + sourceLevel u8 + sourceAbLevel u16 + two pish/pisc groups packing the timing/stack values.
    private static void WriteBuff(PacketStream stream, Buff effect)
    {
        stream.Write(effect.Index);                       // id group: type u32 (buff instance index)
        stream.Write(effect.SkillCaster);                 // caster (SkillCaster sub-struct)
        stream.Write((long)effect.Template.BuffId);       // buffId (i64)
        stream.Write((byte)(effect.Caster?.Level ?? 1));  // sourceLevel u8
        stream.Write((ushort)effect.AbLevel);             // sourceAbLevel u16
        // group 1: totalTime, elapsedTime, tickTime, tickIndex
        stream.WritePisc(new[]
        {
            (uint)effect.Duration,
            (uint)effect.GetTimeElapsed(),
            (uint)effect.Tick,
            0u,
        });
        // group 2: stack, charged, cooldownSkill, reserved
        stream.WritePisc(new uint[] { 1u, 0u, 0u, 0u });
    }
    #endregion NetBuff

    public override string Verbose()
    {
        return " - " + _baseUnitType.ToString() + " - " + _unit?.DebugName();
    }
}
