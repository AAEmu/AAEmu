using System.Numerics;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

public class SummonSlave : Item
{
    private DateTime _repairStartTime;
    public override ItemDetailType DetailType => ItemDetailType.Slave;
    // 10.0.2.13 Slave detail body is 33 bytes (Item_SerializeDetail case 2: total 34).
    // The body is an opaque blob on the client wire; only the leading fields below are interpreted.
    // TODO(v10): decode the trailing bytes from a live capture.
    public override uint DetailBytesLength => 33;

    public byte SlaveType { get; set; } // Not sure about this, captures show 2 here
    public uint SlaveDbId { get; set; }
    public byte IsDestroyed { get; set; }

    public DateTime RepairStartTime
    {
        get => _repairStartTime;
        set
        {
            _repairStartTime = value;
            if (value > DateTime.MinValue)
                IsDestroyed = 0;
        }
    }

    // TODO: Actually use this location for saving the data in ItemDetails
    public Vector3 SummonLocation { get; set; }

    public SummonSlave()
    {
        //
    }

    public SummonSlave(ulong id, ItemTemplate template, int count) : base(id, template, count)
    {
        //
    }

    public override void ReadDetails(PacketStream stream)
    {
        if (stream.LeftBytes < DetailBytesLength)
            return;
        SlaveType = stream.ReadByte(); // Type? (2 = slave?)
        SlaveDbId = stream.ReadBc(); // DbId
        IsDestroyed = stream.ReadByte();
        try
        {
            // Read time of something else than 0
            var timeBytes = stream.ReadBytes(4);
            RepairStartTime = Convert.ToInt32(timeBytes) != 0 ? Convert.ToDateTime(timeBytes) : DateTime.MinValue;

            // Read remaining bytes
            _ = stream.ReadBytes((int)DetailBytesLength - 1 - 4 - 4); // Filler, Equipment?
        }
        catch
        {
            RepairStartTime = DateTime.MinValue;
        }
    }

    public override void WriteDetails(PacketStream stream)
    {
        stream.Write(SlaveType);
        stream.WriteBc(SlaveDbId);
        stream.Write(IsDestroyed);

        if (RepairStartTime == DateTime.MinValue)
            stream.Write(0);
        else
            stream.Write(RepairStartTime);

        // Opaque 10.0.2.13 tail filling the body out to DetailBytesLength. Its leading bytes gate the
        // "recovering" state and (per earlier captures) the summon location; the exact layout is unverified.
        // TODO(v10): decode the tail fields from a live capture.
        stream.Write(new byte[DetailBytesLength - 9]);
    }

    public override void OnManuallyDestroyingItem()
    {
        var owner = WorldManager.Instance.GetCharacterById((uint)OwnerId);
        if (owner == null)
            return;

        if (!owner.ParentWorld.SlaveManager.OnDeleteSlaveItem(this))
            Logger.Warn($"Failed to delete Slave attached to Item Id: {Id}, Type: {TemplateId}");
    }

    public override bool CanDestroy()
    {
        // TODO: Always allow expired items to be removed regardless if summoned or not 
        var owner = WorldManager.Instance.GetCharacterById((uint)OwnerId);
        if (owner != null)
        {
            var checkSlave = owner.ParentWorld.SlaveManager.GetActiveSlaveByOwnerObjId(owner.ObjId);
            if (checkSlave?.Id == SlaveDbId)
            {
                owner.SendErrorMessage(ErrorMessageType.SlaveSpawnItemLocked);
                return false;
            }
        }

        return true;
    }
}
