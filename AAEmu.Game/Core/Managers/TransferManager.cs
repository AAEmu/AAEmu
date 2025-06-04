using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Static;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Transfers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.StaticValues;

using NLog;

namespace AAEmu.Game.Core.Managers;

public class TransferManager
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private bool _initialized = false;
    private object ActiveTransfersLock { get; set; } = new();
    private Dictionary<uint, Transfer> _activeTransfers = [];

    private const double Delay = 100;
    //private const double DelayInit = 1;
    //private Task TransferTickTask { get; set; }

    public void Initialize()
    {
        if (_initialized)
            return;

        Logger.Warn("TransferTickTask: Started");

        //TransferTickTask = new TransferTickStartTask();
        //TaskManager.Instance.Schedule(TransferTickTask, TimeSpan.FromMinutes(DelayInit), TimeSpan.FromMilliseconds(Delay));

        TickManager.Instance.OnTick.Subscribe(TransferTick, TimeSpan.FromMilliseconds(Delay), true);

        _initialized = true;
    }

    private void TransferTick(TimeSpan delta)
    {
        var activeTransfers = GetTransfers();
        foreach (var transfer in activeTransfers)
        {
            transfer.MoveTo(transfer);
        }

        //TaskManager.Instance.Schedule(TransferTickTask, TimeSpan.FromMilliseconds(Delay));
    }

    public void SpawnAll()
    {
        lock (ActiveTransfersLock)
        {
            foreach (var tr in _activeTransfers.Values)
            {
                tr.Spawn();
            }
        }
    }

    public Transfer[] GetTransfers()
    {
        lock (ActiveTransfersLock)
        {
            return _activeTransfers.Values.ToArray();
        }
    }

    public Transfer Create(WorldInstance parentWorld, uint objectId, uint templateId, TransferSpawner spawner)
    {
        /*
        * A sequence of packets when a cart appears:
        * (the wagon itself consists of two parts and two benches for the characters)
        * "Salislead Peninsula ~ Liriot Hillside Loop Carriage"
        * SCUnitStatePacket(tlId0=GetNextId(), objId0=GetNextId(), templateId = 6, modelId = 654, attachPoint=255)
        * "The wagon boarding part"
        * SCUnitStatePacket(tlId2= tlId0, objId2=GetNextId(), templateId = 46, modelId = 653, attachPoint=30, objId=objId0)
        * SCDoodadCreatedPacket(templateId = 5890, attachPoint=2, objId=objId2, x1y1z1)
        * SCDoodadCreatedPacket(templateId = 5890, attachPoint=3, objId=objId2, x2y2z2)
        */

        if (!TransferGameData.Instance.Exist(templateId))
        {
            return null;
        }

        // create a wagon cabin
        var owner = new Transfer();
        owner.ParentWorld = parentWorld;
        var carriage = TransferGameData.Instance.GetTransferTemplate(templateId); // 6 - Salislead Peninsula ~ Liriot Hillside Loop Carriage
        owner.Name = carriage.Name;
        owner.TlId = (ushort)TlIdManager.Instance.GetNextId();
        owner.ObjId = objectId == 0 ? ObjectIdManager.Instance.GetNextId() : objectId;
        owner.OwnerId = 255;
        owner.Spawner = spawner;
        owner.TemplateId = carriage.Id;
        owner.Id = carriage.Id;
        owner.ModelId = carriage.ModelId;
        owner.Template = carriage;
        owner.AttachPointId = AttachPointKind.System;
        owner.BondingObjId = 0;
        owner.Level = 1;
        owner.Hp = owner.MaxHp;
        owner.Mp = owner.MaxMp;
        owner.Bounded = null;
        owner.Transform.ApplyWorldSpawnPosition(spawner.Position);
        owner.Transform.ResetFinalizeTransform();
        owner.Faction = FactionManager.Instance.GetFaction(FactionsEnum.PcFriendly); // formerly set to 164
        owner.Patrol = null;
        // BUFF: Untouchable (Unable to attack this target)
        var buffId = (uint)BuffConstants.Untouchable;
        owner.Buffs.AddBuff(new Buff(owner, owner, SkillCaster.GetByType(SkillCasterType.Unit), SkillManager.Instance.GetBuffTemplate(buffId), null, DateTime.UtcNow));
        owner.Spawn();
        lock (ActiveTransfersLock)
        {
            _activeTransfers.Add(owner.ObjId, owner);
        }

        // Add additional transfer units if defined (like a Carriage/Boarding Part for example)
        if (carriage.TransferBindings.Count <= 0) { return owner; }

        var boardingPart = TransferGameData.Instance.GetTransferTemplate(carriage.TransferBindings[0].TransferId); // 46 - The wagon boarding part
        var transfer = new Transfer();
        transfer.ParentWorld = parentWorld;
        transfer.Name = boardingPart.Name;
        transfer.TlId = owner.TlId; // (ushort)TlIdManager.Instance.GetNextId();
        transfer.ObjId = ObjectIdManager.Instance.GetNextId();
        transfer.OwnerId = owner.ObjId;
        transfer.Spawner = owner.Spawner;
        transfer.TemplateId = boardingPart.Id;
        transfer.Id = boardingPart.Id;
        transfer.ModelId = boardingPart.ModelId;
        transfer.Template = boardingPart;
        transfer.Level = 1;
        // Attach it to master
        transfer.AttachPointId = owner.Template.TransferBindings[0].AttachPointId;
        transfer.BondingObjId = owner.ObjId;
        transfer.Hp = transfer.MaxHp;
        transfer.Mp = transfer.MaxMp;
        transfer.Transform.ApplyWorldSpawnPosition(spawner.Position);
        transfer.Transform.Local.AddDistanceToFront(-9.24417f);
        transfer.Transform.Local.SetHeight(WorldManager.Instance.GetHeight(transfer.Transform));
        transfer.Transform.StickyParent = owner.Transform; // stick it to the driver/motor
        transfer.Transform.Parent = null;
        owner.Transform.Parent = transfer.Transform;
        transfer.Transform.ResetFinalizeTransform();

        transfer.Faction = FactionManager.Instance.GetFaction(FactionsEnum.PcFriendly); // used to be 164

        transfer.Patrol = null;
        // add effect
        buffId = (uint)BuffConstants.Untouchable; // Buff: Unable to attack this target
        transfer.Buffs.AddBuff(new Buff(transfer, transfer, SkillCaster.GetByType(SkillCasterType.Unit), SkillManager.Instance.GetBuffTemplate(buffId), null, DateTime.UtcNow));

        owner.Bounded = transfer; // запомним параметры связанной части в родителе

        transfer.Spawn();
        lock (ActiveTransfersLock)
        {
            _activeTransfers.Add(transfer.ObjId, transfer);
        }

        foreach (var doodadBinding in transfer.Template.TransferBindingDoodads)
        {
            var doodad = DoodadManager.Instance.Create(transfer.ParentWorld, 0, doodadBinding.DoodadId, transfer);
            doodad.Transform.StickyParent = null;
            doodad.Transform.Parent = transfer.Transform;
            doodad.ParentObjId = transfer.ObjId;
            doodad.AttachPoint = doodadBinding.AttachPointId;
            switch (doodadBinding.AttachPointId)
            {
                case AttachPointKind.Passenger0:
                    doodad.Transform.Local.SetPosition(0.00537476f, 5.7852f, 1.36648f, 0, 0, MathF.PI * 2f);
                    break;
                case AttachPointKind.Passenger1:
                    doodad.Transform.Local.SetPosition(0.00537476f, 1.63614f, 1.36648f, 0, 0, 0);
                    break;
            }
            doodad.Transform.ResetFinalizeTransform();
            doodad.PlantTime = DateTime.UtcNow;
            doodad.Data = (byte)doodadBinding.AttachPointId;
            doodad.SetScale(1f);
            doodad.FuncGroupId = doodad.GetFuncGroupId();
            doodad.OwnerType = DoodadOwnerType.System;
            doodad.Spawn();
            transfer.AttachedDoodads.Add(doodad);
        }

        owner.PostUpdateCurrentHp(owner, 0, owner.Hp, KillReason.Unknown);
        transfer.PostUpdateCurrentHp(transfer, 0, transfer.Hp, KillReason.Unknown);

        return owner;
    }

    public void Load()
    {
        lock (ActiveTransfersLock)
            _activeTransfers = [];
    }
}
