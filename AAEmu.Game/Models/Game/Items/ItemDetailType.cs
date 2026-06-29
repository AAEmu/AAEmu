namespace AAEmu.Game.Models.Game.Items;

public enum ItemDetailType
{
    Invalid = 0,
    Equipment = 1,
    Slave = 2,
    Mate = 3,
    Ucc = 4,
    Treasure = 5,
    BigFish = 6,
    Decoration = 7,
    MusicSheet = 8,
    Glider = 9,
    SlaveEquipment = 10,
    Location = 11,
    // Types 12-14 (0xC-0xE) were added in 10.0.2.13 (Item_SerializeDetail). They carry opaque detail
    // blobs on the client wire — the client exposes no field names or detail-type mapping for them, so
    // their semantic names are defined server-side. Bodies: 12 = 10 bytes, 13 = 13 bytes, 14 = 8 bytes
    // (same wire shape as MusicSheet). TODO(v10): name them once the server item-detail factory is known.
    TypeMax = 15,
}
