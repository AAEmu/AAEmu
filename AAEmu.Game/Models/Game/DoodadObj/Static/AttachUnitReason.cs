namespace AAEmu.Game.Models.Game.DoodadObj.Static;

public enum AttachUnitReason : byte
{
    None = 0,
    MountMateLeft = 1,
    MountMateRight = 2,
    MountMateBack = 3,
    MountMateForward = 4,
    SlaveBinding = 5,
    NewMaster = 6,
    BoardTransfer = 7,
    FoundMissedParent = 8,
    InitFromNub = 9,
    PrefabChanged = 10,
    TransferBinding = 11,
    HousingSlaveBinding = 12,
    unk14 = 14,
    SlaveUnbinding = 15
}
