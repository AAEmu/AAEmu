using AAEmu.Game.Models.Game.AI.Abstracts;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.World;

/*
   Author:Sagara, NLObP
*/
namespace AAEmu.Game.Models.Game.AI
{
    public sealed class TransferAi : ACreatureAi
    {
        public TransferAi(GameObject owner, float visibleRange) : base(owner, visibleRange)
        {
        }

        protected override void IamSeeSomeone(GameObject someone)
        {
            //switch (someone.UnitType)
            //{
            //    case BaseUnitType.Character:
            //        var chr = (Character)someone;
            //        var transfer = (Transfer)Owner;
            //        if (transfer.TemplateId != 46 && transfer.TemplateId != 4 && transfer.TemplateId != 122)
            //        {
            //            if (!transfer.IsInPatrol)
            //            {
            //                var path = new Simulation(transfer, transfer.Template.PathSmoothing);
            //                // организуем последовательность "Дорог" для следования "Транспорта", два(0,1) в одну сторону и два(2,3) в обратную
            //                for (var i = 0; i < transfer.Template.TransferRoads.Count; i++)
            //                {
            //                    path.Routes.TryAdd(i, transfer.Template.TransferRoads[i].Pos);
            //                }
            //                path.LoadTransferPathFromRoutes(0); // начнем с самого начала

            //                if (path.Routes[0] != null)
            //                {
            //                    _log.Warn("First spawn myX=" + transfer.Position.X + " myY=" + transfer.Position.Y + " myZ=" + transfer.Position.Z + " rotZ=" + transfer.Rot.Z + " rotationZ=" + transfer.Position.RotationZ);
            //                    transfer.IsInPatrol = true; // so as not to run the route a second time

            //                    // попробуем заспавнить в первой точке пути
            //                    // попробуем смотреть на следующую точку
            //                    var point = path.Routes[0][0];
            //                    var point2 = path.Routes[0][1];
            //                    var angle = MathUtil.CalculateAngleFrom(point.X, point.Y, point2.X, point2.Y);
            //                    var rotZ = MathUtil.ConvertDegreeToDirection(angle);
            //                    path.Angle = MathUtil.DegreeToRadian(angle);
            //                    //path.Angle = MathUtil2.ConvertDegreeToDirectionShort(angle);

            //                    transfer.Position.WorldId = 1;
            //                    transfer.Position.ZoneId = WorldManager.Instance.GetZoneId(transfer.Position.WorldId, point.X, point.Y);
            //                    transfer.Position.X = point.X;
            //                    transfer.Position.Y = point.Y;
            //                    transfer.Position.Z = point.Z;
            //                    transfer.Position.RotationZ = rotZ;
            //                    transfer.Rot = new Quaternion(0f, 0f, (float)path.Angle, 1f);
            //                    transfer.WorldPos = new WorldPos(Helpers.ConvertLongY(point.X), Helpers.ConvertLongY(point.Y), point.Z);
            //                    _log.Warn("New spawn myX={0}, myY={1}, myZ={2}, rotZ={3}, zoneId={4}", transfer.Position.X, transfer.Position.Y, transfer.Position.Z, transfer.Position.RotationZ, transfer.Position.ZoneId);

            //                    path.InPatrol = false;
            //                    path.Loop = true; // повторять бесконечно

            //                    path.GoToPath(transfer, true);
            //                }
            //                else
            //                {
            //                    _log.Warn("PathName: " + transfer.Template.TransferPaths[0].PathName + " not found!");
            //                }
            //            }
            //            //transfer.AddVisibleObject(chr);
            //        }
            //        //if (transfer.TemplateId == 6)
            //        //{
            //        //    TransferManager.Instance.Spawn(chr, transfer);
            //        //    if (!transfer.IsInPatrol)
            //        //    {
            //        //        var path = new Simulation(transfer, transfer.Template.PathSmoothing);
            //        //        //path.ReadPath(transfer.Template.TransferPaths[0].PathName); // пробую файлы от клиента
            //        //        //path.ReadPath("solzreed_3"); // пробую файл от клиента
            //        //        //path.AddPath("solzreed_4"); // пробую добавить файл от клиента

            //        //        //path.LoadAllPath2(transfer.TemplateId, transfer.Position);
            //        //        //if (path.AllRoutes != null)
            //        //        //{
            //        //        //_log.Warn("First spawn myX=" + transfer.Position.X + " myY=" + transfer.Position.Y + " myZ=" + transfer.Position.Z + " rotZ=" + transfer.RotationZ + " rotationZ=" + transfer.Position.RotationZ);
            //        //        //transfer.IsInPatrol = true; // so as not to run the route a second time

            //        //        //_log.Warn("PathName: " + path.AllRouteNames[transfer.TemplateId][0]);
            //        //        //_log.Warn("PathName: " + path.AllRouteNames[transfer.TemplateId][1]);

            //        //        //// попробуем заспавнить в первой точке пути
            //        //        //var point = path.AllRoutes[transfer.TemplateId][0][0];
            //        //        //var point2 = path.AllRoutes[transfer.TemplateId][0][1];

            //        //        //// попробуем смотреть на следующую точку
            //        //        //var angle = MathUtil.CalculateAngleFrom(point.X, point.Y, point2.X, point2.Y);
            //        //        //var rotZ = MathUtil.ConvertDegreeToDirection(angle);

            //        //        //transfer.Position.WorldId = 1;
            //        //        //transfer.Position.ZoneId = WorldManager.Instance.GetZoneId(transfer.Position.WorldId, point.X, point.Y);
            //        //        //transfer.Position.X = point.X;
            //        //        //transfer.Position.Y = point.Y;
            //        //        //transfer.Position.Z = point.Z;
            //        //        //transfer.Position.RotationZ = rotZ;

            //        //        //TransferManager.Instance.Spawn(chr, transfer);
            //        //        //_log.Warn("New spawn myX={0}, myY={1}, myZ={2}, rotZ={3}, zoneId={4}", transfer.Position.X, transfer.Position.Y, transfer.Position.Z, transfer.Position.RotationZ, transfer.Position.ZoneId);

            //        //        //path.GoToPathFromRoutes2(transfer, true);
            //        //        //}

            //        //        // организуем последовательность "Дорог" для следования "Транспорта", два(0,1) в одну сторону и два(2,3) в обратную
            //        //        path.Routes.TryAdd(0, path.LoadPath("lily_solzreed_in1"));
            //        //        path.Routes.TryAdd(1, path.LoadPath("lily_solzreed_in2"));
            //        //        path.Routes.TryAdd(2, path.LoadPath("lilyut_in1"));
            //        //        path.Routes.TryAdd(3, path.LoadPath("lilyut_in2"));
            //        //        path.LoadTransferPathFromRoutes(0);

            //        //        if (path.Routes[0] != null)
            //        //        {
            //        //            _log.Warn("First spawn myX=" + transfer.Position.X + " myY=" + transfer.Position.Y + " myZ=" + transfer.Position.Z + " rotZ=" + transfer.Rot.Z + " rotationZ=" + transfer.Position.RotationZ);
            //        //            transfer.IsInPatrol = true; // so as not to run the route a second time

            //        //            // попробуем заспавнить в первой точке пути
            //        //            // попробуем смотреть на следующую точку
            //        //            var point = path.Routes[0][0];
            //        //            var point2 = path.Routes[0][1];
            //        //            var angle = MathUtil.CalculateAngleFrom(point.X, point.Y, point2.X, point2.Y);
            //        //            var rotZ = MathUtil.ConvertDegreeToDirection(angle);
            //        //            transfer.Position.WorldId = 1;
            //        //            transfer.Position.ZoneId = WorldManager.Instance.GetZoneId(transfer.Position.WorldId, point.X, point.Y);
            //        //            transfer.Position.X = point.X;
            //        //            transfer.Position.Y = point.Y;
            //        //            transfer.Position.Z = point.Z;
            //        //            transfer.Position.RotationZ = rotZ;
            //        //            transfer.WorldPos = new WorldPos(Helpers.ConvertLongY(point.X), Helpers.ConvertLongY(point.Y), point.Z);
            //        //            TransferManager.Instance.Spawn(chr, transfer);
            //        //            _log.Warn("New spawn myX={0}, myY={1}, myZ={2}, rotZ={3}, zoneId={4}", transfer.Position.X, transfer.Position.Y, transfer.Position.Z, transfer.Position.RotationZ, transfer.Position.ZoneId);

            //        //            path.GoToPath(transfer, true);
            //        //        }
            //        //        else
            //        //        {
            //        //            _log.Warn("PathName: " + transfer.Template.TransferPaths[0].PathName + " not found!");
            //        //        }
            //        //    }
            //        //}

            //        //if (transfer.TemplateId == 8)
            //        //{
            //        //    TransferManager.Instance.Spawn(chr, transfer);
            //        //    if (!transfer.IsInPatrol)
            //        //    {
            //        //        var path = new Simulation(transfer, transfer.Template.PathSmoothing);
            //        //        // организуем последовательность "Дорог" для следования "Транспорта", два(0,1) в одну сторону и два(2,3) в обратную
            //        //        path.Routes.TryAdd(0, path.LoadPath("gwe_goto_lilyut1"));
            //        //        path.Routes.TryAdd(1, path.LoadPath("gwe_goto_lilyut2"));
            //        //        path.Routes.TryAdd(2, path.LoadPath("gwe_goto_lilyut3"));
            //        //        path.Routes.TryAdd(3, path.LoadPath("lillyut_goto_gwe1"));
            //        //        path.Routes.TryAdd(4, path.LoadPath("lillyut_goto_gwe2"));
            //        //        path.Routes.TryAdd(5, path.LoadPath("lillyut_goto_gwe3"));
            //        //        path.LoadTransferPathFromRoutes(0);

            //        //        if (path.Routes[0] != null)
            //        //        {
            //        //            _log.Warn("First spawn myX=" + transfer.Position.X + " myY=" + transfer.Position.Y + " myZ=" + transfer.Position.Z + " rotZ=" + transfer.Rot.Z + " rotationZ=" + transfer.Position.RotationZ);
            //        //            transfer.IsInPatrol = true; // so as not to run the route a second time

            //        //            // попробуем заспавнить в первой точке пути
            //        //            // попробуем смотреть на следующую точку
            //        //            var point = path.Routes[0][0];
            //        //            var point2 = path.Routes[0][1];
            //        //            var angle = MathUtil.CalculateAngleFrom(point.X, point.Y, point2.X, point2.Y);
            //        //            var rotZ = MathUtil.ConvertDegreeToDirection(angle);
            //        //            transfer.Position.WorldId = 1;
            //        //            transfer.Position.ZoneId = WorldManager.Instance.GetZoneId(transfer.Position.WorldId, point.X, point.Y);
            //        //            transfer.Position.X = point.X;
            //        //            transfer.Position.Y = point.Y;
            //        //            transfer.Position.Z = point.Z;
            //        //            transfer.Position.RotationZ = rotZ;
            //        //            transfer.WorldPos = new WorldPos(Helpers.ConvertLongY(point.X), Helpers.ConvertLongY(point.Y), point.Z);
            //        //            TransferManager.Instance.Spawn(chr, transfer);
            //        //            _log.Warn("New spawn myX={0}, myY={1}, myZ={2}, rotZ={3}, zoneId={4}", transfer.Position.X, transfer.Position.Y, transfer.Position.Z, transfer.Position.RotationZ, transfer.Position.ZoneId);

            //        //            path.GoToPath(transfer, true);
            //        //        }
            //        //        else
            //        //        {
            //        //            _log.Warn("PathName: " + transfer.Template.TransferPaths[0].PathName + " not found!");
            //        //        }
            //        //    }
            //        //}

            //        //if (transfer.TemplateId == 52)
            //        //{
            //        //    if (!transfer.IsInPatrol)
            //        //    {
            //        //        var path = new Simulation(transfer, transfer.Template.PathSmoothing);
            //        //        // организуем последовательность "Дорог" для следования "Транспорта", два(0,1) в одну сторону и два(2,3) в обратную
            //        //        //path.Routes.TryAdd(0, path.LoadPath("gwe_out_goto_lilyut1"));
            //        //        //path.Routes.TryAdd(1, path.LoadPath("lilyut_goto_solz"));
            //        //        ////path.Routes.TryAdd(2, path.LoadPath("solz_out2_milkyvill"));
            //        //        //path.Routes.TryAdd(2, path.LoadPath("solz_in2_solzrean"));
            //        //        //path.Routes.TryAdd(3, path.LoadPath("solz_out1_solzrean"));
            //        //        ////path.Routes.TryAdd(5, path.LoadPath("solz_in1_milkyvill"));
            //        //        //path.Routes.TryAdd(4, path.LoadPath("lilyut_goto_gwe"));
            //        //        //path.Routes.TryAdd(5, path.LoadPath("gwe_in1"));

            //        //        for (var i = 0; i < transfer.Template.TransferRoads.Count; i++)
            //        //        {
            //        //            path.Routes.TryAdd(i, transfer.Template.TransferRoads[i].Pos);
            //        //        }

            //        //        path.LoadTransferPathFromRoutes(0); // начнем с самого начала

            //        //        if (path.Routes[0] != null)
            //        //        {
            //        //            _log.Warn("First spawn myX=" + transfer.Position.X + " myY=" + transfer.Position.Y + " myZ=" + transfer.Position.Z + " rotZ=" + transfer.Rot.Z + " rotationZ=" + transfer.Position.RotationZ);
            //        //            transfer.IsInPatrol = true; // so as not to run the route a second time

            //        //            //// попробуем заспавнить в первой точке пути
            //        //            //// попробуем смотреть на следующую точку
            //        //            //var point = path.Routes[0][0];
            //        //            //var point2 = path.Routes[0][1];
            //        //            //var angle = MathUtil.CalculateAngleFrom(point.X, point.Y, point2.X, point2.Y);
            //        //            //var rotZ = MathUtil.ConvertDegreeToDirection(angle);
            //        //            ////path.rad = MathUtil.DegreeToRadian(angle);
            //        //            //path.Angle = MathUtil2.ConvertDegreeToDirectionShort(angle);

            //        //            //transfer.Position.WorldId = 1;
            //        //            //transfer.Position.ZoneId = WorldManager.Instance.GetZoneId(transfer.Position.WorldId, point.X, point.Y);
            //        //            //transfer.Position.X = point.X;
            //        //            //transfer.Position.Y = point.Y;
            //        //            //transfer.Position.Z = point.Z;
            //        //            //transfer.Position.RotationZ = rotZ;
            //        //            //var axis = new Vector3(0f, 0f, 1f);
            //        //            //transfer.Rot = new Quaternion(0f, 0f, (float)path.Angle, 1f);
            //        //            //transfer.WorldPos = new WorldPos(Helpers.ConvertLongY(point.X), Helpers.ConvertLongY(point.Y), point.Z);
            //        //            //transfer.AddVisibleObject(chr);
            //        //            _log.Warn("New spawn myX={0}, myY={1}, myZ={2}, rotZ={3}, zoneId={4}", transfer.Position.X, transfer.Position.Y, transfer.Position.Z, transfer.Position.RotationZ, transfer.Position.ZoneId);

            //        //            path.InPatrol = false;

            //        //            path.GoToPath(transfer, true);
            //        //        }
            //        //        else
            //        //        {
            //        //            _log.Warn("PathName: " + transfer.Template.TransferPaths[0].PathName + " not found!");
            //        //        }
            //        //    }
            //        //}
            //        //if (transfer.TemplateId == 65)
            //        //{
            //        //    //TransferManager.Instance.Spawn(chr, transfer);
            //        //    //transfer.AddVisibleObject(chr);

            //        //    if (!transfer.IsInPatrol)
            //        //    {
            //        //        var path = new Simulation(transfer, transfer.Template.PathSmoothing);
            //        //        // организуем последовательность "Дорог" для следования "Транспорта"
            //        //        path.Routes.TryAdd(0, path.LoadPath("air_white_garang_out_1"));
            //        //        path.Routes.TryAdd(1, path.LoadPath("air_white_garang_out_2"));
            //        //        path.Routes.TryAdd(2, path.LoadPath("air_whiteB_sandB"));
            //        //        path.Routes.TryAdd(3, path.LoadPath("air_sandB_royster"));
            //        //        path.Routes.TryAdd(4, path.LoadPath("air_royster_sandB"));
            //        //        path.Routes.TryAdd(5, path.LoadPath("air_sandB_whiteB"));
            //        //        path.Routes.TryAdd(6, path.LoadPath("air_garang_white_in_1"));
            //        //        path.Routes.TryAdd(7, path.LoadPath("air_garang_white_in_2"));
            //        //        path.LoadTransferPathFromRoutes(0); // air_white_garang_out_1

            //        //        if (path.Routes[0] != null)
            //        //        {
            //        //            _log.Warn("First spawn myX=" + transfer.Position.X + " myY=" + transfer.Position.Y + " myZ=" + transfer.Position.Z + " rotZ=" + transfer.Rot.Z + " rotationZ=" + transfer.Position.RotationZ);
            //        //            transfer.IsInPatrol = true; // so as not to run the route a second time

            //        //            // попробуем заспавнить в первой точке пути
            //        //            // попробуем смотреть на следующую точку
            //        //            //var point = path.Routes[0][0];
            //        //            //var point2 = path.Routes[0][1];
            //        //            //var angle = MathUtil.CalculateAngleFrom(point.X, point.Y, point2.X, point2.Y);
            //        //            //var rotZ = MathUtil.ConvertDegreeToDirection(angle);
            //        //            //path.Angle = MathUtil2.ConvertDegreeToDirectionShort(angle);

            //        //            //transfer.Position.WorldId = 1;
            //        //            //transfer.Position.ZoneId = WorldManager.Instance.GetZoneId(transfer.Position.WorldId, point.X, point.Y);
            //        //            //transfer.Position.X = point.X;
            //        //            //transfer.Position.Y = point.Y;
            //        //            //transfer.Position.Z = point.Z;
            //        //            //transfer.Position.RotationZ = rotZ;
            //        //            //var axis = new Vector3(0f, 0f, 1f);
            //        //            //transfer.Rot = new Quaternion(0f, 0f, (float)path.Angle, 1f);

            //        //            //transfer.WorldPos = new WorldPos(Helpers.ConvertLongY(point.X), Helpers.ConvertLongY(point.Y), point.Z);
            //        //            ////TransferManager.Instance.Spawn(chr, transfer);
            //        //            //transfer.AddVisibleObject(chr);
            //        //            _log.Warn("New spawn myX={0}, myY={1}, myZ={2}, rotZ={3}, zoneId={4}", transfer.Position.X, transfer.Position.Y, transfer.Position.Z, transfer.Position.RotationZ, transfer.Position.ZoneId);

            //        //            path.InPatrol = false;

            //        //            path.GoToPath(transfer, true);
            //        //        }
            //        //        else
            //        //        {
            //        //            _log.Warn("PathName: " + transfer.Template.TransferPaths[0].PathName + " not found!");
            //        //        }
            //        //    }
            //        //}

            //        break;
            //    case BaseUnitType.Npc:
            //        break;
            //    case BaseUnitType.Slave:
            //        break;
            //    case BaseUnitType.Housing:
            //        break;
            //    case BaseUnitType.Transfer:
            //        break;
            //    case BaseUnitType.Mate:
            //        break;
            //    case BaseUnitType.Shipyard:
            //        break;
            //    default:
            //        throw new ArgumentOutOfRangeException();
            //}
        }

        protected override void IamUnseeSomeone(GameObject someone)
        {
            //switch (someone.UnitType)
            //{
            //    case BaseUnitType.Character:
            //        var chr = (Character)someone;
            //        var transfer = (Transfer)Owner;
            //        transfer.RemoveVisibleObject(chr);
            //        break;
            //}
        }

        protected override void SomeoneSeeMe(GameObject someone)
        {
            //switch (someone.UnitType)
            //{
            //    case BaseUnitType.Character:
            //        var chr = (Character)someone;
            //        var transfer = (Transfer)Owner;
            //        transfer.AddVisibleObject(chr);
            //        break;
            //}
        }

        protected override void SomeoneUnseeMee(GameObject someone)
        {
            //switch (someone.UnitType)
            //{
            //    case BaseUnitType.Character:
            //        var chr = (Character)someone;
            //        var transfer = (Transfer)Owner;
            //        transfer.RemoveVisibleObject(chr);
            //        break;
            //}
        }

        protected override void SomeoneThatIamSeeWasMoved(GameObject someone, MovementAction action)
        {
            //switch (someone.UnitType)
            //{
            //    case BaseUnitType.Character:
            //        var chr = (Character)someone;
            //        var transfer = (Transfer)Owner;
            //        transfer.AddVisibleObject(chr);
            //        break;
            //}
        }

        protected override void SomeoneThatSeeMeWasMoved(GameObject someone, MovementAction action)
        {
            //switch (someone.UnitType)
            //{
            //    case BaseUnitType.Character:
            //        var chr = (Character)someone;
            //        var transfer = (Transfer)Owner;
            //        //TransferManager.Instance.Spawn(chr, transfer);
            //        transfer.AddVisibleObject(chr);
            //        break;
            //}
        }
    }
}
