using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// u32 exp, i32 spawnDelayTime and exactly ten i32 mount-skill ids.
/// </summary>
/// <remarks>
/// Two fields moved in 10.0.2.13, measured against the three the binary names (itemId, userState,
/// spawnDelayTime): a byte joins the front of the block, and the pair between userState and
/// spawnDelayTime became a single u32, dropping mileage. Writing the v1.2 shape put every mount-skill
/// walks to resolve each mount_skills row — held whatever the misalignment landed on, and no mount
/// showed any skills. The length stayed close enough that the packet still parsed.
/// </remarks>
public class SCMateSpawnedPacket(Mate mate) : GamePacket(SCOffsets.SCMateSpawnedPacket, 1)
{
    private const int MountSkillSlots = 10;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(mate.TlId);

        // enum_mate_types (1 ride, 2 battle), resolved through the npc's mate_equip_slot_pack. The
        // typed 0 is "none", which leaves it with neither the ride nor the battle skill set.
        stream.Write(mate.MateType);
        stream.Write(mate.Id);
        stream.Write(mate.ItemId);
        stream.Write(mate.UserState);
        stream.Write((uint)mate.Experience);
        stream.Write(mate.SpawnDelayTime);

        // The client reads a fixed ten ids and resolves each against mount_skills, so short lists
        // are padded rather than counted.
        for (var i = 0; i < MountSkillSlots; i++)
            stream.Write(i < mate.Skills.Count ? mate.Skills[i] : 0u);

        return stream;
    }
}
