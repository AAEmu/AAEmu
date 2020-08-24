using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Tasks.UnitMove;
using AAEmu.Game.Utils;

using NLog;

using Point = AAEmu.Game.Models.Game.World.Point;

namespace AAEmu.Game.Models.Game.Units.Route
{
    /// <summary>
    /// Control NPC to move along this route
    /// </summary>
    public class Simulation : Patrol
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();

        public Character character;
        public Npc npc;
        public Transfer transfer;

        public bool AbandonTo { get; set; } = false; // для прерывания repeat()
        // +++
        private float maxVelocityForward;
        private float maxVelocityBackward;
        private float velAccel;
        private float angVel;
        private int Steering;
        //
        public double yaw = -90; // degree
        public double angle;
        public double angleTmp;
        public Vector3 vPosition;
        public Vector3 vTarget;
        public Vector3 vDistance;
        public Vector3 vVelocity;
        public Vector3 direction;
        public float distance;
        public DateTime gameTime;
        // movement data
        public List<string> MovePath;     //  the data we're going to be moving on at the moment
        public List<Point> TransferPath;  // path from client file
        public List<string> RecordPath;   // data to write the path
        public Dictionary<int, List<Point>> Routes; // Steering, TransferPath
        public Dictionary<uint, Dictionary<int, List<Point>>> AllRoutes; // templateId, Steering, TransferPath
        public Dictionary<uint, Dictionary<int, string>> AllRouteNames; // templateId, Steering, TransferPath
        // +++
        public int PointsCount { get; set; }              // number of points in the process of recording the path
        public bool SavePathEnabled { get; set; }         // flag, path recording
        public bool MoveToPathEnabled { get; set; }       // flag, road traffic
        public bool MoveToForward { get; set; }           // movement direction true -> forward, true -> back
        public bool RunningMode { get; set; } = false;    // movement mode true -> run, true -> walk
        public bool Move { get; set; } = false;           // movement mode true -> moving to the point #, false -> get next point #
        public int MoveStepIndex { get; set; }            // current checkpoint (where are we running now)

        private float oldX, oldY, oldZ;
        //*******************************************************
        public string RecordFilesPath = @"./Data/Path/"; // path where our files are stored
        public string RecordFileExt = @".path";          // default extension
        public string MoveFilesPath = @"./Data/Path/";   // path where our files are stored
        public string MoveFileExt = @".path";            // default extension
        public string MoveFileName = "";                 // default name
        private float MovingDistance = 0.25f; //0.3f;
        private float tempMovingDistance;
        private float RangeToCheckPoint = 0.5f; // distance to checkpoint at which it is considered that we have reached it
        private int MoveTrigerDelay = 1000;     // triggering the timer for movement 1 sec

        //*******************************************************
        /*
           by alexsl
           a little mite in scripting, someone might need it.
           
           what they're doing:
           - automatically writes the route to the file;
           - you can load the path data from the file;
           - moves along the route.
           
           To start with, you need to create a route(s), the recording takes place as follows:
           1. Start recording - "rec";
           2. Walk along the route;
           3. stop recording - "save".
           === here is an approximate file structure (x,y,z)=========.
           |15629,0|14989,02|141,2055|
           |15628,0|14987,24|141,3826|
           |15626,0|14983,88|141,3446|
           ==================================================
           */
        //***************************************************************
        public Simulation(Unit unit, float velocityForward = 8.0f, float velocityBackward = -5.0f, float velAcceleration = 0.5f, float angVelocity = 1.0f)
        {
            if (unit is Transfer)
            {
                Routes = new Dictionary<int, List<Point>>();
                velAccel = velAcceleration; //per s
                maxVelocityForward = velocityForward;
                maxVelocityBackward = velocityForward;
                velAccel = velAcceleration;
                angVel = angVelocity;
                Steering = 0;
                //var maxVelBackward = -2.0f; //per s
                //var diffX = 0f;
                //var diffY = 0f;
                //var diffZ = 0f;
            }
            Init(unit);
        }
        //***************************************************************
        //public override void Execute(Unit unit)
        //{
        //    if (unit is Npc npc)
        //    {
        //        NextPathOrPointInPath(npc);
        //    }
        //    else if (unit is Transfer transfer)
        //    {
        //        NextPathOrPointInPath(transfer);
        //    }
        //}
        //***************************************************************
        //MOVEMENT:
        //Go to point with coordinates x, y, z
        //MOVETO(npc, x, y, z)

        //***************************************************************
        // returns the distance between 2 points
        public int Delta(float vPositionX1, float vPositionY1, float vPositionX2, float vPositionY2)
        {
            //return Math.Round(Math.Sqrt((vPositionX1-vPositionX2)*(vPositionX1-vPositionX2))+(vPositionY1-vPositionY2)*(vPositionY1-vPositionY2));
            var dx = vPositionX1 - vPositionX2;
            var dy = vPositionY1 - vPositionY2;
            var summa = dx * dx + dy * dy;
            if (Math.Abs(summa) < Tolerance)
            {
                return 0;
            }

            return (int)Math.Round(Math.Sqrt(summa));
        }
        //***************************************************************
        // Orientation on the terrain: Check if the given point is within reach
        //public bool PosInRange(Npc npc, float targetX, float targetY, float targetZ, int distance)
        //***************************************************************
        public bool PosInRange(Unit unit, float targetX, float targetY, int distance)
        {
            if (unit is Npc npc)
            {
                return Delta(targetX, targetY, npc.Position.X, npc.Position.Y) <= distance;
            }

            if (unit is Transfer transfer)
            {
                return Delta(targetX, targetY, transfer.Position.X, transfer.Position.Y) <= distance;
            }

            return false;
        }
        //***************************************************************
        public string GetValue(string valName)
        {
            return RecordPath.Find(x => x == valName);
        }
        //***************************************************************
        public void SetValue(string valName, string value)
        {
            var index = RecordPath.IndexOf(RecordPath.Where(x => x == valName).FirstOrDefault());
            RecordPath[index] = value;
        }
        //***************************************************************
        public float ExtractValue(string sData, int nIndex)
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            int i;
            var j = 0;
            var s = sData;
            while (j < nIndex)
            {
                i = s.IndexOf('|');
                if (i >= 0)
                {
                    s = s.Substring(i + 1, s.Length - (i + 1));
                    j++;
                }
                else
                {
                    break;
                }
            }
            i = s.IndexOf('|');
            if (i >= 0)
            {
                s = s.Substring(0, i - 1);
            }
            var result = Convert.ToSingle(s);
            return result;
        }
        //***************************************************************
        public int GetMinCheckPoint(Unit unit, List<string> pointsList)
        {
            string s;
            var index = -1;

            // check for a route
            if (pointsList.Count == 0)
            {
                //_log.Warn("no data on the route.");
                return -1;
            }

            if (unit is Npc npc)
            {
                int m, minDist;
                minDist = -1;
                for (var i = 0; i < pointsList.Count; i++)
                {
                    s = pointsList[i];
                    vPosition.X = ExtractValue(s, 1);
                    vPosition.Y = ExtractValue(s, 2);
                    vPosition.Z = ExtractValue(s, 3);

                    _log.Warn(s + " #" + i + " x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);

                    m = Delta(vPosition.X, vPosition.Y, npc.Position.X, npc.Position.Y);

                    if (index == -1)
                    {
                        minDist = m;
                        index = i;
                    }
                    else if (m < minDist)
                    {
                        minDist = m;
                        index = i;
                    }
                }
            }
            else if (unit is Transfer transfer)
            {
                float delta;
                var minDist = 0f;
                vTarget = new Vector3(transfer.Position.X, transfer.Position.Y, transfer.Position.Z);
                for (var i = 0; i < pointsList.Count; i++)
                {
                    s = pointsList[i];
                    vPosition = new Vector3(ExtractValue(s, 1), ExtractValue(s, 2), ExtractValue(s, 3));

                    _log.Warn(s + " x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);

                    delta = MathUtil2.GetDistance(vTarget, vPosition);

                    if (index == -1) // first assignment
                    {
                        index = i;
                        minDist = delta;
                    }
                    if (delta < minDist) // save if less
                    {
                        index = i;
                        minDist = delta;
                    }
                }
            }

            return index;
        }
        //***************************************************************
        public (int, int) GetMinCheckPointFromRoutes(Unit unit)
        {
            var pointIndex = 0;
            var routeIndex = 0;
            for (var i = 0; i < Routes.Count; i++)
            {
                pointIndex = GetMinCheckPointFromRoutes(unit, Routes[i]);
                if (pointIndex == -1) { continue; }

                routeIndex = i;
                break; // нашли нужную точку res в "пути" с индексом index
            }
            return (pointIndex, routeIndex);
        }
        //***************************************************************
        public (int, int) GetMinCheckPointFromRoutes2(Transfer transfer)
        {
            var pointIndex = 0;
            var routeIndex = 0;
            //for (var i = 0; i < AllRoutes[transfer.TemplateId].Count; i++)
            foreach (var (id, routes) in AllRoutes)
            {
                foreach (var (idx, route) in routes)
                {
                    pointIndex = GetMinCheckPointFromRoutes(transfer, route);
                    if (pointIndex == -1) { continue; }
                    routeIndex = idx;
                    break; // нашли нужную точку pointIndex в участке пути с индексом routeIndex
                }
            }
            return (pointIndex, routeIndex);
        }
        //***************************************************************
        public int GetMinCheckPointFromRoutes(Unit unit, List<Point> pointsList, float distance = 200f)
        {
            var pointIndex = -1;
            // check for a route
            if (pointsList.Count == 0)
            {
                _log.Warn("no route data.");
                return -1;
            }
            if (unit is Transfer transfer)
            {
                float delta;
                var minDist = 0f;
                vTarget = new Vector3(transfer.Position.X, transfer.Position.Y, transfer.Position.Z);
                for (var i = 0; i < pointsList.Count; i++)
                {
                    vPosition = new Vector3(pointsList[i].X, pointsList[i].Y, pointsList[i].Z);

                    _log.Warn("#" + i + " x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);

                    delta = MathUtil2.GetDistance(vTarget, vPosition);

                    if (delta > distance) { continue; } // ищем точку не очень далеко от повозки

                    if (pointIndex == -1) // first assignment
                    {
                        pointIndex = i;
                        minDist = delta;
                    }
                    if (delta < minDist) // save if less
                    {
                        pointIndex = i;
                        minDist = delta;
                    }
                }
            }

            return pointIndex;
        }
        //***************************************************************
        public int GetMinCheckPoint(Unit unit, List<Point> pointsList)
        {
            var index = -1;
            // check for a route
            if (pointsList.Count == 0)
            {
                _log.Warn("no route data.");
                return -1;
            }
            if (unit is Npc npc)
            {
                int m, minDist;
                minDist = -1;
                for (var i = 0; i < pointsList.Count; i++)
                {
                    vPosition.X = pointsList[i].X;
                    vPosition.Y = pointsList[i].Y;
                    vPosition.Z = pointsList[i].Z;

                    _log.Warn("#" + i + " x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);

                    m = Delta(vPosition.X, vPosition.Y, npc.Position.X, npc.Position.Y);

                    if (m <= 0) { continue; }

                    if (index == -1)
                    {
                        minDist = m;
                        index = i;
                    }
                    else if (m < minDist)
                    {
                        minDist = m;
                        index = i;
                    }
                }
            }
            else if (unit is Transfer transfer)
            {
                float delta;
                var minDist = 0f;
                vTarget = new Vector3(transfer.Position.X, transfer.Position.Y, transfer.Position.Z);
                for (var i = 0; i < pointsList.Count; i++)
                {
                    vPosition = new Vector3(pointsList[i].X, pointsList[i].Y, pointsList[i].Z);

                    _log.Warn("#" + i + " x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);

                    delta = MathUtil2.GetDistance(vTarget, vPosition);
                    if (delta > 200) { continue; } // ищем точку не очень далеко от повозки

                    if (index == -1) // first assignment
                    {
                        index = i;
                        minDist = delta;
                    }
                    if (delta < minDist) // save if less
                    {
                        index = i;
                        minDist = delta;
                    }
                }
            }

            return index;
        }
        //***************************************************************
        public void StartRecord(Simulation sim, Character ch)
        {
            if (SavePathEnabled) { return; }
            if (MoveToPathEnabled)
            {
                _log.Warn("while following the route, recording is not possible.");
                return;
            }
            RecordPath.Clear();
            PointsCount = 0;
            _log.Warn("route recording started ...");
            SavePathEnabled = true;
            RepeatTo(ch, MoveTrigerDelay, sim);
        }
        //***************************************************************
        public void Record(Simulation sim, Character ch)
        {
            //if (!SavePathEnabled) { return; }
            var s = "|" + ch.Position.X + "|" + ch.Position.Y + "|" + ch.Position.Z + "|";
            RecordPath.Add(s);
            PointsCount++;
            _log.Warn("added checkpoint # {0}", PointsCount);
            RepeatTo(ch, MoveTrigerDelay, sim);
        }
        //***************************************************************
        public void StopRecord(Simulation sim)
        {
            // write to file
            using (var sw = new StreamWriter(GetRecordFileName()))
            {
                foreach (var b in RecordPath)
                {
                    sw.WriteLine(b.ToString());
                }
            }
            _log.Warn("Route recording completed.");
            SavePathEnabled = false;
        }
        //***************************************************************
        public string GetRecordFileName()
        {
            var result = RecordFilesPath + MoveFileName + RecordFileExt;
            return result;
        }
        //***************************************************************
        public string GetMoveFileName()
        {
            var result = MoveFilesPath + MoveFileName + MoveFileExt;
            return result;
        }
        //***************************************************************
        public List<Point> GetTransferPath(int index = 0)
        {
            TransferPath = Routes[index];
            return TransferPath;
        }
        //***************************************************************
        public void LoadTransferPath(int index = 0)
        {
            TransferPath = Routes[index];
        }
        //***************************************************************
        public void LoadTransferPath2(uint id, int index = 0)
        {
            TransferPath = AllRoutes[id][index];
        }
        //***************************************************************
        public void LoadAllPath(Point position)
        {
            Routes = TransferManager.Instance.GetAllTransferPath(position);

        }
        //***************************************************************
        public void LoadAllPath2(uint templateId, Point position, byte worldId = 1)
        {
            (AllRoutes, AllRouteNames) = TransferManager.Instance.GetAllTransferPath2(templateId);
        }
        //***************************************************************
        public void ParseMoveClient(Unit unit)
        {
            if (!SavePathEnabled) { return; }
            if (unit is Npc npc)
            {
                vPosition.X = npc.Position.X;
                vPosition.Y = npc.Position.Y;
                vPosition.Z = npc.Position.Z;
            }
            else if (unit is Transfer transfer)
            {
                vPosition.X = transfer.Position.X;
                vPosition.Y = transfer.Position.Y;
                vPosition.Z = transfer.Position.Z;
            }
            var s = "|" + vPosition.X + "|" + vPosition.Y + "|" + vPosition.Z + "|";
            RecordPath.Add(s);
            PointsCount++;
            //_log.Warn("added checkpoint # {0}", PointsCount);
        }
        //***************************************************************
        public void GoToPathFromRoutes(Unit unit, bool toForward = true)
        {
            if (!(unit is Transfer transfer)) { return; }

            if (Routes.Count > 0) // имеются пути для дорог?
            {
                MoveToPathEnabled = !MoveToPathEnabled;
                MoveToForward = toForward;
                if (!MoveToPathEnabled || transfer.Position == null || !transfer.IsInPatrol)
                {
                    _log.Warn("the route is stopped.");
                    StopMove(transfer);
                    return;
                }

                // presumably the path is already registered in MovePath
                _log.Warn("trying to get on the road...");
                // first go to the closest checkpoint
                //var i = GetMinCheckPoint(transfer, TransferPath);
                var (msi, idx) = GetMinCheckPointFromRoutes(transfer);
                if (msi < 0)
                {
                    _log.Warn("no checkpoint found.");
                    StopMove(transfer);
                    return;
                }
                Steering = idx; // индекс нужного "пути" в списке дорог
                //LoadTransferPath(idx);   // загружаем "путь" в TransferPath
                LoadTransferPath2(transfer.TemplateId, idx);   // загружаем "путь" в TransferPath
                _log.Warn("found index routes # " + idx + " nearest checkpoint # " + msi + " walk there ...");
                _log.Warn("index #" + idx);
                _log.Warn("checkpoint #" + msi);
                _log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);

                MoveToPathEnabled = true;
                MoveStepIndex = msi; // текущая точка пути
                var s = TransferPath[MoveStepIndex]; // текущий "путь" с текущим индексом точки куда идти
                vPosition.X = s.X;
                vPosition.Y = s.Y;
                vPosition.Z = s.Z;

                if (Math.Abs(oldX - vPosition.X) > Tolerance && Math.Abs(oldY - vPosition.Y) > Tolerance && Math.Abs(oldZ - vPosition.Z) > Tolerance)
                {
                    oldX = vPosition.X;
                    oldY = vPosition.Y;
                    oldZ = vPosition.Z;
                }
            }
            RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z);
        }
        public void GoToPathFromRoutes2(Transfer transfer, bool toForward = true)
        {
            if (AllRoutes[transfer.TemplateId].Count > 0) // имеются участки пути для дорог?
            {
                MoveToPathEnabled = !MoveToPathEnabled;
                MoveToForward = toForward;
                if (!MoveToPathEnabled || transfer.Position == null || !transfer.IsInPatrol)
                {
                    _log.Warn("the route is stopped.");
                    StopMove(transfer);
                    return;
                }

                // presumably the path is already registered in MovePath
                _log.Warn("trying to get on the road...");
                // first go to the closest checkpoint
                //var i = GetMinCheckPoint(transfer, TransferPath);
                var (pointIndex, routeIndex) = GetMinCheckPointFromRoutes2(transfer);
                if (pointIndex == -1)
                {
                    _log.Warn("no checkpoint found.");
                    StopMove(transfer);
                    return;
                }
                Steering = routeIndex; // индекс нужного участка пути в списке дорог
                LoadTransferPath2(transfer.TemplateId, routeIndex);   // загружаем участок пути в TransferPath
                _log.Warn("found index routes # " + routeIndex + " nearest checkpoint # " + pointIndex + " walk there ...");
                _log.Warn("index #" + routeIndex);
                _log.Warn("checkpoint #" + pointIndex);
                _log.Warn("x={0}, y={1}, z={2}, rotZ={3}, zoneId={4}", transfer.Position.X, transfer.Position.Y, transfer.Position.Z, transfer.Position.RotationZ, transfer.Position.ZoneId);

                MoveToPathEnabled = true;
                MoveStepIndex = pointIndex; // текущая точка пути
                var s = TransferPath[MoveStepIndex]; // текущий участок пути с текущим индексом точки куда идти
                vPosition.X = s.X;
                vPosition.Y = s.Y;
                vPosition.Z = s.Z;

                if (Math.Abs(oldX - vPosition.X) > Tolerance && Math.Abs(oldY - vPosition.Y) > Tolerance && Math.Abs(oldZ - vPosition.Z) > Tolerance)
                {
                    oldX = vPosition.X;
                    oldY = vPosition.Y;
                    oldZ = vPosition.Z;
                }
            }
            RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z);
        }
        public void GoToPath(Unit unit, bool ToForward)
        {
            if (unit is Npc npc)
            {
                if (MovePath.Count > 0)
                {
                    MoveToPathEnabled = !MoveToPathEnabled;
                    MoveToForward = ToForward;
                    if (!MoveToPathEnabled)
                    {
                        _log.Warn("the route is stopped.");
                        StopMove(npc);
                        return;
                    }

                    // presumably the path is already registered in MovePath
                    _log.Warn("trying to get on the path ...");
                    // first go to the closest checkpoint
                    var i = GetMinCheckPoint(npc, MovePath);
                    if (i < 0)
                    {
                        _log.Warn("checkpoint not found.");
                        StopMove(npc);
                        return;
                    }

                    _log.Warn("found nearest checkpoint # " + i + " run there ...");
                    MoveToPathEnabled = true;
                    MoveStepIndex = i;
                    _log.Warn("checkpoint #" + i);
                    var s = MovePath[MoveStepIndex];
                    vPosition.X = ExtractValue(s, 1);
                    vPosition.Y = ExtractValue(s, 2);
                    vPosition.Z = ExtractValue(s, 3);

                    if (Math.Abs(oldX - vPosition.X) > Tolerance && Math.Abs(oldY - vPosition.Y) > Tolerance && Math.Abs(oldZ - vPosition.Z) > Tolerance)
                    {
                        oldX = vPosition.X;
                        oldY = vPosition.Y;
                        oldZ = vPosition.Z;
                    }
                }
                if (TransferPath.Count > 0)
                {
                    MoveToPathEnabled = !MoveToPathEnabled;
                    MoveToForward = ToForward;
                    if (!MoveToPathEnabled)
                    {
                        _log.Warn("the route is stopped.");
                        StopMove(npc);
                        return;
                    }

                    // presumably the path is already registered in MovePath
                    _log.Warn("trying to get on the path ...");
                    // first go to the closest checkpoint
                    var i = GetMinCheckPoint(npc, TransferPath);
                    if (i < 0)
                    {
                        _log.Warn("checkpoint not found.");
                        StopMove(npc);
                        return;
                    }

                    _log.Warn("found nearest checkpoint # " + i + " run there ...");
                    _log.Warn(" #" + i + "x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                    MoveToPathEnabled = true;
                    MoveStepIndex = i;
                    _log.Warn("checkpoint #" + i);
                    _log.Warn(" #" + i + "x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                    var s = TransferPath[MoveStepIndex];
                    vPosition.X = s.X;
                    vPosition.Y = s.Y;
                    vPosition.Z = s.Z;

                    if (Math.Abs(oldX - vPosition.X) > Tolerance && Math.Abs(oldY - vPosition.Y) > Tolerance && Math.Abs(oldZ - vPosition.Z) > Tolerance)
                    {
                        oldX = vPosition.X;
                        oldY = vPosition.Y;
                        oldZ = vPosition.Z;
                    }
                }
                RepeatMove(this, npc, vPosition.X, vPosition.Y, vPosition.Z);
            }
            else if (unit is Transfer transfer)
            {
                if (MovePath.Count > 0)
                {
                    MoveToPathEnabled = !MoveToPathEnabled;
                    MoveToForward = ToForward;
                    if (!MoveToPathEnabled)
                    {
                        _log.Warn("the route has been stopped.");
                        StopMove(transfer);
                        return;
                    }

                    // presumably the path is already registered in MovePath
                    _log.Warn("trying to get on the road...");
                    // first go to the closest checkpoint
                    var i = GetMinCheckPoint(transfer, MovePath);
                    if (i < 0)
                    {
                        _log.Warn("no checkpoint found.");
                        StopMove(transfer);
                        return;
                    }

                    _log.Warn("found nearest checkpoint # " + i + " walk there ...");
                    MoveToPathEnabled = true;
                    MoveStepIndex = i;
                    _log.Warn("checkpoint #" + i);
                    var s = MovePath[MoveStepIndex];
                    vPosition.X = ExtractValue(s, 1);
                    vPosition.Y = ExtractValue(s, 2);
                    vPosition.Z = ExtractValue(s, 3);

                    if (Math.Abs(oldX - vPosition.X) > Tolerance && Math.Abs(oldY - vPosition.Y) > Tolerance && Math.Abs(oldZ - vPosition.Z) > Tolerance)
                    {
                        oldX = vPosition.X;
                        oldY = vPosition.Y;
                        oldZ = vPosition.Z;
                    }
                }
                if (TransferPath.Count > 0)
                {
                    MoveToPathEnabled = !MoveToPathEnabled;
                    MoveToForward = ToForward;
                    if (!MoveToPathEnabled || transfer.Position == null || !transfer.IsInPatrol)
                    {
                        _log.Warn("the route is stopped.");
                        StopMove(transfer);
                        return;
                    }

                    // presumably the path is already registered in MovePath
                    _log.Warn("trying to get on the road...");
                    // first go to the closest checkpoint
                    var i = GetMinCheckPoint(transfer, TransferPath);
                    if (i < 0)
                    {
                        _log.Warn("no checkpoint found.");
                        StopMove(transfer);
                        return;
                    }

                    _log.Warn("found nearest checkpoint # " + i + " walk there ...");
                    _log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                    MoveToPathEnabled = true;
                    MoveStepIndex = i;
                    _log.Warn("checkpoint #" + i);
                    _log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                    var s = TransferPath[MoveStepIndex];
                    vPosition.X = s.X;
                    vPosition.Y = s.Y;
                    vPosition.Z = s.Z;

                    if (Math.Abs(oldX - vPosition.X) > Tolerance && Math.Abs(oldY - vPosition.Y) > Tolerance && Math.Abs(oldZ - vPosition.Z) > Tolerance)
                    {
                        oldX = vPosition.X;
                        oldY = vPosition.Y;
                        oldZ = vPosition.Z;
                    }
                }
                RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z, transfer.Template.TransferPaths[Steering].WaitTimeStart * 1000);
            }
        }
        //***************************************************************
        public void MoveTo(Simulation sim, Unit unit, float targetX, float targetY, float targetZ)
        {
            if (unit is Npc npc)
            {
                if (!npc.IsInPatrol)
                {
                    StopMove(npc);
                    return;
                }
                var move = false;
                var x = npc.Position.X - targetX;
                var y = npc.Position.Y - targetY;
                var z = npc.Position.Z - targetZ;
                var MaxXYZ = Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Abs(z));

                if (RunningMode)
                {
                    MovingDistance = 0.5f;
                }
                else
                {
                    MovingDistance = 0.25f;
                }


                if (Math.Abs(x) > RangeToCheckPoint)
                {
                    if (Math.Abs(MaxXYZ - Math.Abs(x)) > Tolerance)
                    {
                        tempMovingDistance = Math.Abs(x) / (MaxXYZ / MovingDistance);
                        tempMovingDistance = Math.Min(tempMovingDistance, MovingDistance);
                    }
                    else
                    {
                        tempMovingDistance = MovingDistance;
                    }

                    if (x < 0)
                    {
                        npc.Position.X += tempMovingDistance;
                    }
                    else
                    {
                        npc.Position.X -= tempMovingDistance;
                    }
                    if (Math.Abs(x) < tempMovingDistance)
                    {
                        npc.Position.X = vPosition.X;
                    }
                    move = true;
                }
                if (Math.Abs(y) > RangeToCheckPoint)
                {
                    if (Math.Abs(MaxXYZ - Math.Abs(y)) > Tolerance)
                    {
                        tempMovingDistance = Math.Abs(y) / (MaxXYZ / MovingDistance);
                        tempMovingDistance = Math.Min(tempMovingDistance, MovingDistance);
                    }
                    else
                    {
                        tempMovingDistance = MovingDistance;
                    }
                    if (y < 0)
                    {
                        npc.Position.Y += tempMovingDistance;
                    }
                    else
                    {
                        npc.Position.Y -= tempMovingDistance;
                    }
                    if (Math.Abs(y) < tempMovingDistance)
                    {
                        npc.Position.Y = vPosition.Y;
                    }
                    move = true;
                }
                if (Math.Abs(z) > RangeToCheckPoint)
                {
                    if (Math.Abs(MaxXYZ - Math.Abs(z)) > Tolerance)
                    {
                        tempMovingDistance = Math.Abs(z) / (MaxXYZ / MovingDistance);
                        tempMovingDistance = Math.Min(tempMovingDistance, MovingDistance);
                    }
                    else
                    {
                        tempMovingDistance = MovingDistance;
                    }
                    if (z < 0)
                    {
                        npc.Position.Z += tempMovingDistance;
                    }
                    else
                    {
                        npc.Position.Z -= tempMovingDistance;
                    }
                    if (Math.Abs(z) < tempMovingDistance)
                    {
                        npc.Position.Z = vPosition.Z;
                    }
                    move = true;
                }
                // simulation unit. return the moveType object
                var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
                // Change the NPC coordinates
                moveType.X = npc.Position.X;
                moveType.Y = npc.Position.Y;
                moveType.Z = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;
                // looks in the direction of movement
                //-----------------------взгляд_NPC_будет(движение_откуда->движение_куда)
                angle = MathUtil.CalculateAngleFrom(npc.Position.X, npc.Position.Y, vPosition.X, vPosition.Y);
                var rotZ = MathUtil.ConvertDegreeToDirection(angle);
                moveType.RotationX = 0;
                moveType.RotationY = 0;
                moveType.RotationZ = rotZ;
                if (RunningMode)
                {
                    moveType.Flags = 4;      // 5-walk, 4-run, 3-stand still
                }
                else
                {
                    moveType.Flags = 5;      // 5-walk, 4-run, 3-stand still
                }
                moveType.DeltaMovement = new sbyte[3];
                moveType.DeltaMovement[0] = 0;
                moveType.DeltaMovement[1] = 127;
                moveType.DeltaMovement[2] = 0;
                moveType.Stance = 1;     // COMBAT = 0x0, IDLE = 0x1
                moveType.Alertness = 0;  // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
                moveType.Time += 100;    // has to change all the time for normal motion.
                if (move)
                {
                    // moving to the point #
                    npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                    RepeatMove(sim, npc, targetX, targetY, targetZ);
                }
                else
                {
                    OnMove(npc);
                }
            }
            else if (unit is Transfer transfer)
            {
                if (!MoveToPathEnabled || transfer.Position == null || !transfer.IsInPatrol)
                {
                    transfer.Throttle = 0;
                    StopMove(transfer);
                    return;
                }

                // transfer.gameTime = DateTime.Now;
                vTarget = new Vector3(targetX, targetY, targetZ);

                // create a pointer where we are going
                var spwnFlag = SpawnFlag(vTarget);

                vPosition = new Vector3(transfer.Position.X, transfer.Position.Y, transfer.Position.Z);
                vDistance = vPosition - vTarget; // dx, dy, dz
                // distance to the point where we are moving
                distance = MathUtil2.GetDistance(vPosition, vTarget);

                // get current values
                // var deltaTime = (float)(DateTime.Now - GameService.StartTime).TotalSeconds;
                var deltaTime = 0.05f; // temporarily took a constant, later it will be necessary to take the current time

                var velocityX = vVelocity.X;
                var velocityY = vVelocity.Y;
                var velocityZ = vVelocity.Z;
                var linInertia = 0.3f;    //per s   // TODO Move to the upper motion control module
                var linDeaccelInertia = 0.1f;  //per s   // TODO Move to the upper motion control module
                // accelerate to maximum speed
                transfer.Speed += velAccel * deltaTime;
                // check that it is not more than the maximum forward or reverse speed
                transfer.Speed = Math.Clamp(transfer.Speed, maxVelocityBackward, maxVelocityForward);
                // find out how far we have traveled over the past period of time
                MovingDistance = transfer.Speed * deltaTime;
                // calculate the distance that can be walked along each coordinate for a period of time
                var rad = MathUtil2.CalculateDirection(vPosition, vTarget);
                var cosA = (float)Math.Cos(rad);
                var sinA = (float)Math.Sin(rad);
                var newX = MovingDistance * cosA;
                var newY = MovingDistance * sinA;

                // var newPosition = Vector3.Lerp(vPosition, vTarget, transfer.Speed * deltaTime);
                // var newX = newPosition.X - vPosition.X;
                // var newY = newPosition.Y - vPosition.Y;

                // if the distance in X to travel is greater than the distance that can be traveled in a period of time
                if (Math.Abs(vDistance.X) >= Math.Abs(newX))
                {
                    transfer.Position.X += newX;
                    transfer.VelX = (short)(transfer.Speed * cosA * 1000);
                }
                else
                {
                    transfer.Position.X = vTarget.X;
                }
                // if the distance in Y to travel is greater than the distance that can be traveled in a period of time
                if (Math.Abs(vDistance.Y) >= Math.Abs(newY))
                {
                    transfer.Position.Y += newY;
                    transfer.VelY = (short)(transfer.Speed * sinA * 1000);
                }
                else
                {
                    transfer.Position.Y = vTarget.Y;
                }
                var newPosition = Vector3.Lerp(vPosition, vTarget, deltaTime);
                transfer.Position.Z = newPosition.Z;
                // transfer.Position.Z = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(transfer.Position.ZoneId, vPosition.X, vPosition.Y) : targetZ;

                // looks in the direction of movement
                angleTmp = MathUtil.RadianToDegree(rad);
                // slowly turn to the desired angle
                if (angleTmp > 0)
                {
                    angle += angVel;
                    angle = (float)Math.Clamp(angle, 0f, angleTmp);
                }
                else
                {
                    angle -= angVel;
                    angle = (float)Math.Clamp(angle, angleTmp, 0f);
                }
                // var rotZ = MathUtil2.ConvertDegreeToDirectionShort(angle);
                var rotZ = MathUtil.UpdateHeading(angle);
                transfer.RotationZ = rotZ;
                transfer.Position.RotationZ = MathUtil.ConvertDegreeToDirection(angle);
                transfer.RotationZ = Helpers.ConvertRotation(transfer.Position.RotationZ);
                transfer.AngVelZ = (float)rad; //angVel; // сюда записывать дельту, в радианах, угла поворота между начальным вектором и конечным
                // _log.Warn("Rad={0}, angle={1}, rotZ={2}", rad, angle, rotZ);

                // despawnCross(spawnCrossList);
                DespawnFlag(spwnFlag);

                if (distance > RangeToCheckPoint)
                {
                    //update class variables
                    vVelocity = new Vector3(velocityX, velocityY, velocityZ);
                    // update Transfer variable
                    transfer.PathPointIndex = MoveStepIndex;
                    transfer.Steering = Steering;
                    // moving to the point #
                    var moveType = (TransferMoveType)MoveType.GetType(MoveTypeEnum.Transfer);
                    moveType.UseTransferBase(transfer);
                    transfer.BroadcastPacket(new SCOneUnitMovementPacket(transfer.ObjId, moveType), true);
                    RepeatMove(sim, transfer, targetX, targetY, targetZ);
                }
                else
                {
                    // get next path or point # in current path
                    // NextPathOrPointInPath(transfer);
                    OnMove(transfer);
                }
            }
        }
        //***************************************************************
        public Doodad spawnFlag(float posX, float posY)
        {
            // spawn flag
            var _combatFlag = new DoodadSpawner();
            _combatFlag.Id = 0;
            _combatFlag.UnitId = 5014; // Combat Flag Id=5014;
            _combatFlag.Position = new Point();
            _combatFlag.Position.ZoneId = WorldManager.Instance.GetZoneId(1, posX, posY);
            _combatFlag.Position.WorldId = 1;
            _combatFlag.Position.X = posX;
            _combatFlag.Position.Y = posY;
            _combatFlag.Position.Z = WorldManager.Instance.GetHeight(_combatFlag.Position.ZoneId, _combatFlag.Position.X, _combatFlag.Position.Y);
            return _combatFlag.Spawn(0); // set CombatFlag
        }
        //***************************************************************
        public List<Doodad> SpawnCross(Vector3 pos)
        {
            var ld = new List<Doodad>();
            // spawn cross
            var _crossFlag = new DoodadSpawner();
            _crossFlag.Id = 0;
            _crossFlag.UnitId = 5014; // Combat Flag Id=5014;
            _crossFlag.Position = new Point();
            _crossFlag.Position.ZoneId = WorldManager.Instance.GetZoneId(1, pos.X, pos.Y);
            _crossFlag.Position.WorldId = 1;
            _crossFlag.Position.X = pos.X;
            _crossFlag.Position.Y = pos.Y;
            _crossFlag.Position.Z = WorldManager.Instance.GetHeight(_crossFlag.Position.ZoneId, _crossFlag.Position.X, _crossFlag.Position.Y);
            var dd = _crossFlag.Spawn(0);
            ld.Add(dd);
            _crossFlag = new DoodadSpawner();
            _crossFlag.Id = 0;
            _crossFlag.UnitId = 5014; // Combat Flag Id=5014;
            _crossFlag.Position = new Point();
            _crossFlag.Position.ZoneId = WorldManager.Instance.GetZoneId(1, pos.X, pos.Y);
            _crossFlag.Position.WorldId = 1;
            _crossFlag.Position.X = pos.X + 5;
            _crossFlag.Position.Y = pos.Y;
            _crossFlag.Position.Z = WorldManager.Instance.GetHeight(_crossFlag.Position.ZoneId, _crossFlag.Position.X, _crossFlag.Position.Y);
            dd = _crossFlag.Spawn(0);
            ld.Add(dd);
            _crossFlag = new DoodadSpawner();
            _crossFlag.Id = 0;
            _crossFlag.UnitId = 5014; // Combat Flag Id=5014;
            _crossFlag.Position = new Point();
            _crossFlag.Position.ZoneId = WorldManager.Instance.GetZoneId(1, pos.X, pos.Y);
            _crossFlag.Position.WorldId = 1;
            _crossFlag.Position.X = pos.X + 10;
            _crossFlag.Position.Y = pos.Y;
            _crossFlag.Position.Z = WorldManager.Instance.GetHeight(_crossFlag.Position.ZoneId, _crossFlag.Position.X, _crossFlag.Position.Y);
            dd = _crossFlag.Spawn(0);
            ld.Add(dd);
            _crossFlag = new DoodadSpawner();
            _crossFlag.Id = 0;
            _crossFlag.UnitId = 5014; // Combat Flag Id=5014;
            _crossFlag.Position = new Point();
            _crossFlag.Position.ZoneId = WorldManager.Instance.GetZoneId(1, pos.X, pos.Y);
            _crossFlag.Position.WorldId = 1;
            _crossFlag.Position.X = pos.X - 5;
            _crossFlag.Position.Y = pos.Y;
            _crossFlag.Position.Z = WorldManager.Instance.GetHeight(_crossFlag.Position.ZoneId, _crossFlag.Position.X, _crossFlag.Position.Y);
            dd = _crossFlag.Spawn(0);
            ld.Add(dd);
            _crossFlag = new DoodadSpawner();
            _crossFlag.Id = 0;
            _crossFlag.UnitId = 5014; // Combat Flag Id=5014;
            _crossFlag.Position = new Point();
            _crossFlag.Position.ZoneId = WorldManager.Instance.GetZoneId(1, pos.X, pos.Y);
            _crossFlag.Position.WorldId = 1;
            _crossFlag.Position.X = pos.X - 10;
            _crossFlag.Position.Y = pos.Y;
            _crossFlag.Position.Z = WorldManager.Instance.GetHeight(_crossFlag.Position.ZoneId, _crossFlag.Position.X, _crossFlag.Position.Y);
            dd = _crossFlag.Spawn(0);
            ld.Add(dd);
            _crossFlag = new DoodadSpawner();
            _crossFlag.Id = 0;
            _crossFlag.UnitId = 5014; // Combat Flag Id=5014;
            _crossFlag.Position = new Point();
            _crossFlag.Position.ZoneId = WorldManager.Instance.GetZoneId(1, pos.X, pos.Y);
            _crossFlag.Position.WorldId = 1;
            _crossFlag.Position.X = pos.X;
            _crossFlag.Position.Y = pos.Y + 5;
            _crossFlag.Position.Z = WorldManager.Instance.GetHeight(_crossFlag.Position.ZoneId, _crossFlag.Position.X, _crossFlag.Position.Y);
            dd = _crossFlag.Spawn(0);
            ld.Add(dd);
            _crossFlag = new DoodadSpawner();
            _crossFlag.Id = 0;
            _crossFlag.UnitId = 5014; // Combat Flag Id=5014;
            _crossFlag.Position = new Point();
            _crossFlag.Position.ZoneId = WorldManager.Instance.GetZoneId(1, pos.X, pos.Y);
            _crossFlag.Position.WorldId = 1;
            _crossFlag.Position.X = pos.X;
            _crossFlag.Position.Y = pos.Y + 10;
            _crossFlag.Position.Z = WorldManager.Instance.GetHeight(_crossFlag.Position.ZoneId, _crossFlag.Position.X, _crossFlag.Position.Y);
            dd = _crossFlag.Spawn(0);
            ld.Add(dd);
            _crossFlag = new DoodadSpawner();
            _crossFlag.Id = 0;
            _crossFlag.UnitId = 5014; // Combat Flag Id=5014;
            _crossFlag.Position = new Point();
            _crossFlag.Position.ZoneId = WorldManager.Instance.GetZoneId(1, pos.X, pos.Y);
            _crossFlag.Position.WorldId = 1;
            _crossFlag.Position.X = pos.X;
            _crossFlag.Position.Y = pos.Y - 5;
            _crossFlag.Position.Z = WorldManager.Instance.GetHeight(_crossFlag.Position.ZoneId, _crossFlag.Position.X, _crossFlag.Position.Y);
            dd = _crossFlag.Spawn(0);
            ld.Add(dd);
            _crossFlag = new DoodadSpawner();
            _crossFlag.Id = 0;
            _crossFlag.UnitId = 5014; // Combat Flag Id=5014;
            _crossFlag.Position = new Point();
            _crossFlag.Position.ZoneId = WorldManager.Instance.GetZoneId(1, pos.X, pos.Y);
            _crossFlag.Position.WorldId = 1;
            _crossFlag.Position.X = pos.X;
            _crossFlag.Position.Y = pos.Y - 10;
            _crossFlag.Position.Z = WorldManager.Instance.GetHeight(_crossFlag.Position.ZoneId, _crossFlag.Position.X, _crossFlag.Position.Y);
            dd = _crossFlag.Spawn(0);
            ld.Add(dd);

            return ld; // set CombatFlag
        }
        //***************************************************************
        public void DespawnCross(List<Doodad> doodadList)
        {
            // despawn cross Flag
            var _crossFlag = new DoodadSpawner();
            foreach (var doodad in doodadList)
            {
                _crossFlag.Despawn(doodad);
            }
        }
        //***************************************************************
        public Doodad SpawnFlag(Vector3 pos)
        {
            // spawn flag
            var _combatFlag = new DoodadSpawner();
            _combatFlag.Id = 0;
            _combatFlag.UnitId = 5014; // Combat Flag Id=5014;
            _combatFlag.Position = new Point();
            _combatFlag.Position.ZoneId = WorldManager.Instance.GetZoneId(1, pos.X, pos.Y);
            _combatFlag.Position.WorldId = 1;
            _combatFlag.Position.X = pos.X;
            _combatFlag.Position.Y = pos.Y;
            _combatFlag.Position.Z = WorldManager.Instance.GetHeight(_combatFlag.Position.ZoneId, _combatFlag.Position.X, _combatFlag.Position.Y);
            return _combatFlag.Spawn(0); // set CombatFlag
        }
        //***************************************************************
        public void DespawnFlag(Doodad doodad)
        {
            // spawn flag
            var _combatFlag = new DoodadSpawner();
            _combatFlag.Despawn(doodad);
        }
        //***************************************************************
        public void RepeatMove(Simulation sim, Unit unit, float TargetX, float TargetY, float TargetZ, double time = 50)
        {
            if (unit is Npc npc)
            {
                //if ((sim ?? this).AbandonTo)
                {
                    TaskManager.Instance.Schedule(new Move(sim ?? this, npc, TargetX, TargetY, TargetZ), TimeSpan.FromMilliseconds(time));
                }
            }
            else if (unit is Transfer transfer)
            {
                //if ((sim ?? this).AbandonTo)
                {
                    TaskManager.Instance.Schedule(new Move(sim ?? this, transfer, TargetX, TargetY, TargetZ), TimeSpan.FromMilliseconds(time));
                }
            }
        }
        //***************************************************************
        public void RepeatTo(Character ch, double time = 1000, Simulation sim = null)
        {
            if ((sim ?? this).SavePathEnabled)
            {
                TaskManager.Instance.Schedule(new Record(sim ?? this, ch), TimeSpan.FromMilliseconds(time));
            }
        }
        //***************************************************************
        public void StopMove(Unit unit)
        {
            if (unit is Npc npc)
            {
                _log.Warn("stop moving ...");
                var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
                moveType.X = npc.Position.X;
                moveType.Y = npc.Position.Y;
                moveType.Z = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;
                //-----------------------взгляд_NPC_будет(движение_откуда->движение_куда)
                var angle = MathUtil.CalculateAngleFrom(npc.Position.X, npc.Position.Y, vPosition.X, vPosition.Y);
                var rotZ = MathUtil.ConvertDegreeToDirection(angle);
                moveType.RotationX = 0;
                moveType.RotationY = 0;
                moveType.RotationZ = rotZ;
                moveType.Flags = 5;      // 5-walk, 4-run, 3-stand still
                moveType.DeltaMovement = new sbyte[3];
                moveType.DeltaMovement[0] = 0;
                moveType.DeltaMovement[1] = 0;
                moveType.DeltaMovement[2] = 0;
                moveType.Stance = 1;     // COMBAT = 0x0, IDLE = 0x1
                moveType.Alertness = 0;  // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
                moveType.Time += 50;    // has to change all the time for normal motion.
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                MoveToPathEnabled = false;
            }
            else if (unit is Transfer transfer)
            {
                _log.Warn("stop moving ...");
                transfer.Speed = 0;
                transfer.RotSpeed = 0;
                transfer.VelX = 0;
                transfer.VelY = 0;
                transfer.VelZ = 0;
                vVelocity = Vector3.Zero;
                var moveType = (TransferMoveType)MoveType.GetType(MoveTypeEnum.Transfer);
                moveType.UseTransferBase(transfer);
                transfer.BroadcastPacket(new SCOneUnitMovementPacket(transfer.ObjId, moveType), true);
                MoveToPathEnabled = false;
            }
        }
        public void PauseMove(Unit unit)
        {
            if (unit is Npc npc)
            {
                _log.Warn("let's stand a little...");
                var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
                moveType.X = npc.Position.X;
                moveType.Y = npc.Position.Y;
                moveType.Z = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;
                //-----------------------взгляд_NPC_будет(движение_откуда->движение_куда)
                var angle = MathUtil.CalculateAngleFrom(npc.Position.X, npc.Position.Y, vPosition.X, vPosition.Y);
                var rotZ = MathUtil.ConvertDegreeToDirection(angle);
                moveType.RotationX = 0;
                moveType.RotationY = 0;
                moveType.RotationZ = rotZ;
                moveType.Flags = 5;      // 5-walk, 4-run, 3-stand still
                moveType.DeltaMovement = new sbyte[3];
                moveType.DeltaMovement[0] = 0;
                moveType.DeltaMovement[1] = 0;
                moveType.DeltaMovement[2] = 0;
                moveType.Stance = 1;     // COMBAT = 0x0, IDLE = 0x1
                moveType.Alertness = 0;  // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
                moveType.Time += 50;    // has to change all the time for normal motion.
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
            }
            else if (unit is Transfer transfer)
            {
                _log.Warn("let's stand a little...");
                _log.Warn("pause in #" + MoveStepIndex);
                _log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                transfer.Speed = 0;
                transfer.RotSpeed = 0;
                transfer.RotSpeed = 0;
                transfer.VelX = 0;
                transfer.VelY = 0;
                transfer.VelZ = 0;
                vVelocity = Vector3.Zero;
                var moveType = (TransferMoveType)MoveType.GetType(MoveTypeEnum.Transfer);
                moveType.UseTransferBase(transfer);
                transfer.BroadcastPacket(new SCOneUnitMovementPacket(transfer.ObjId, moveType), true);
            }
        }
        public void OnMove(BaseUnit unit)
        {
            if (unit is Npc npc)
            {
                if (!MoveToPathEnabled)
                {
                    _log.Warn("OnMove disabled");
                    StopMove(npc);
                    return;
                }
                if (MovePath.Count > 0)
                {
                    var s = MovePath[MoveStepIndex];
                    vPosition.X = ExtractValue(s, 1);
                    vPosition.Y = ExtractValue(s, 2);
                    vPosition.Z = ExtractValue(s, 3);
                    if (!PosInRange(npc, vPosition.X, vPosition.Y, 3))
                    {
                        RepeatMove(this, npc, vPosition.X, vPosition.Y, vPosition.Z);
                        return;
                    }

                    if (MoveToForward)
                    {
                        if (MoveStepIndex == MovePath.Count - 1)
                        {
                            _log.Warn("we are ideally at the end point.");
                            //StopMove(npc);
                            PauseMove(npc);
                            MoveToForward = false; //turn back
                            MoveStepIndex--;
                            _log.Warn("walk to #" + MoveStepIndex);
                            s = MovePath[MoveStepIndex];
                            vPosition.X = ExtractValue(s, 1);
                            vPosition.Y = ExtractValue(s, 2);
                            vPosition.Z = ExtractValue(s, 3);
                            RepeatMove(this, npc, vPosition.X, vPosition.Y, vPosition.Z, 20000);
                            return;
                        }

                        MoveStepIndex++;
                        _log.Warn("we have reached checkpoint go on...");
                    }
                    else
                    {
                        if (MoveStepIndex > 0)
                        {
                            MoveStepIndex--;
                            _log.Warn("we reached checkpoint go further ...");
                        }
                        else
                        {
                            _log.Warn("we are ideally at the starting point.");
                            //StopMove(npc);
                            PauseMove(npc);
                            MoveToForward = true; //turn back
                            MoveStepIndex++;
                            _log.Warn("walk to #" + MoveStepIndex);
                            s = MovePath[MoveStepIndex];
                            vPosition.X = ExtractValue(s, 1);
                            vPosition.Y = ExtractValue(s, 2);
                            vPosition.Z = ExtractValue(s, 3);
                            RepeatMove(this, npc, vPosition.X, vPosition.Y, vPosition.Z, 20000);
                            return;
                        }
                    }

                    _log.Warn("walk to #" + MoveStepIndex);
                    s = MovePath[MoveStepIndex];
                    vPosition.X = ExtractValue(s, 1);
                    vPosition.Y = ExtractValue(s, 2);
                    vPosition.Z = ExtractValue(s, 3);
                    RepeatMove(this, npc, vPosition.X, vPosition.Y, vPosition.Z);
                }
                if (TransferPath.Count > 0)
                {
                    var s = TransferPath[MoveStepIndex];
                    vPosition.X = s.X;
                    vPosition.Y = s.Y;
                    vPosition.Z = s.Z;
                    if (!PosInRange(npc, vPosition.X, vPosition.Y, 3))
                    {
                        RepeatMove(this, npc, vPosition.X, vPosition.Y, vPosition.Z);
                        return;
                    }

                    if (MoveToForward)
                    {
                        if (MoveStepIndex == TransferPath.Count - 1)
                        {
                            _log.Warn("we are ideally at the end point.");
                            //StopMove(npc);
                            PauseMove(npc);
                            MoveToForward = false; //turn back
                            MoveStepIndex--;
                            _log.Warn("walk to #" + MoveStepIndex);
                            s = TransferPath[MoveStepIndex];
                            vPosition.X = s.X;
                            vPosition.Y = s.Y;
                            vPosition.Z = s.Z;
                            RepeatMove(this, npc, vPosition.X, vPosition.Y, vPosition.Z, 20000);
                            return;
                        }

                        MoveStepIndex++;
                        _log.Warn("we have reached checkpoint go on...");
                    }
                    else
                    {
                        if (MoveStepIndex > 0)
                        {
                            MoveStepIndex--;
                            _log.Warn("we reached checkpoint go further ...");
                        }
                        else
                        {
                            _log.Warn("we are ideally at the starting point.");
                            //StopMove(npc);
                            PauseMove(npc);
                            MoveToForward = true; //turn back
                            MoveStepIndex++;
                            _log.Warn("walk to #" + MoveStepIndex);
                            s = TransferPath[MoveStepIndex];
                            vPosition.X = s.X;
                            vPosition.Y = s.Y;
                            vPosition.Z = s.Z;
                            RepeatMove(this, npc, vPosition.X, vPosition.Y, vPosition.Z, 20000);
                            return;
                        }
                    }

                    _log.Warn("walk to #" + MoveStepIndex);
                    s = TransferPath[MoveStepIndex];
                    vPosition.X = s.X;
                    vPosition.Y = s.Y;
                    vPosition.Z = s.Z;
                    RepeatMove(this, npc, vPosition.X, vPosition.Y, vPosition.Z);
                }
            }
            else if (unit is Transfer transfer)
            {
                if (!MoveToPathEnabled)
                {
                    _log.Warn("OnMove disabled");
                    StopMove(transfer);
                    return;
                }
                if (MovePath.Count > 0)
                {
                    var s = MovePath[MoveStepIndex];
                    vPosition.X = ExtractValue(s, 1);
                    vPosition.Y = ExtractValue(s, 2);
                    vPosition.Z = ExtractValue(s, 3);
                    if (!PosInRange(transfer, vPosition.X, vPosition.Y, 3))
                    {
                        RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z);
                        return;
                    }

                    if (MoveToForward)
                    {
                        if (MoveStepIndex == MovePath.Count - 1)
                        {
                            _log.Warn("we are at the end point.");
                            //StopMove(npc);
                            PauseMove(transfer);
                            MoveToForward = false; //turn back
                            MoveStepIndex--;
                            _log.Warn("walk to #" + MoveStepIndex);
                            s = MovePath[MoveStepIndex];
                            vPosition.X = ExtractValue(s, 1);
                            vPosition.Y = ExtractValue(s, 2);
                            vPosition.Z = ExtractValue(s, 3);
                            RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z, transfer.Template.TransferPaths[Steering].WaitTimeEnd * 1000);
                            return;
                        }

                        MoveStepIndex++;
                        _log.Warn("we reached checkpoint go further...");
                    }
                    else
                    {
                        if (MoveStepIndex > 0)
                        {
                            MoveStepIndex--;
                            _log.Warn("we reached checkpoint go further ...");
                        }
                        else
                        {
                            _log.Warn("we are at the starting point.");
                            //StopMove(npc);
                            PauseMove(transfer);
                            MoveToForward = true; //turn back
                            MoveStepIndex++;
                            _log.Warn("walk to #" + MoveStepIndex);
                            s = MovePath[MoveStepIndex];
                            vPosition.X = ExtractValue(s, 1);
                            vPosition.Y = ExtractValue(s, 2);
                            vPosition.Z = ExtractValue(s, 3);
                            RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z, transfer.Template.TransferPaths[Steering].WaitTimeStart * 1000);
                            return;
                        }
                    }

                    _log.Warn("walk to #" + MoveStepIndex);
                    s = MovePath[MoveStepIndex];
                    vPosition.X = ExtractValue(s, 1);
                    vPosition.Y = ExtractValue(s, 2);
                    vPosition.Z = ExtractValue(s, 3);
                    RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z);
                }
                if (TransferPath.Count > 0)
                {
                    var s = TransferPath[MoveStepIndex];
                    vPosition.X = s.X;
                    vPosition.Y = s.Y;
                    vPosition.Z = s.Z;
                    if (PosInRange(transfer, vPosition.X, vPosition.Y, 50))
                    {
                        if (MoveToForward)
                        {
                            if (MoveStepIndex == TransferPath.Count - 1)
                            {
                                _log.Warn("we are at the end point.");
                                //StopMove(npc);
                                PauseMove(transfer);
                                MoveToForward = false; //turn back
                                MoveStepIndex--;
                                _log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                                _log.Warn("walk to #" + MoveStepIndex);
                                s = TransferPath[MoveStepIndex];
                                vPosition.X = s.X;
                                vPosition.Y = s.Y;
                                vPosition.Z = s.Z;
                                RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z, transfer.Template.TransferPaths[Steering].WaitTimeEnd * 1000);
                                return;
                            }
                            MoveStepIndex++;
                            _log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                            _log.Warn("we reached checkpoint go further...");
                        }
                        else
                        {
                            if (MoveStepIndex > 0)
                            {
                                MoveStepIndex--;
                                _log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                                _log.Warn("we reached checkpoint go further...");
                            }
                            else
                            {
                                _log.Warn("we are at the starting point.");
                                //StopMove(npc);
                                PauseMove(transfer);
                                MoveToForward = true; //turn back
                                MoveStepIndex++;
                                _log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                                _log.Warn("walk to #" + MoveStepIndex);
                                s = TransferPath[MoveStepIndex];
                                vPosition.X = s.X;
                                vPosition.Y = s.Y;
                                vPosition.Z = s.Z;
                                RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z, transfer.Template.TransferPaths[Steering].WaitTimeStart * 1000);
                                return;
                            }
                        }
                        _log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                        _log.Warn("walk to #" + MoveStepIndex);
                        s = TransferPath[MoveStepIndex];
                        vPosition.X = s.X;
                        vPosition.Y = s.Y;
                        vPosition.Z = s.Z;
                        RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z);
                        return;
                    }
                    _log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                    _log.Warn("to any point is very far, we stop.");
                }
            }
        }
        public void NextPathOrPointInPath(Unit unit)
        {
            if (unit is Npc npc)
            {
                if (!MoveToPathEnabled)
                {
                    _log.Warn("Move disabled");
                    StopMove(npc);
                    return;
                }
                if (MovePath.Count > 0)
                {
                    var s = MovePath[MoveStepIndex];
                    vPosition.X = ExtractValue(s, 1);
                    vPosition.Y = ExtractValue(s, 2);
                    vPosition.Z = ExtractValue(s, 3);
                    if (!PosInRange(npc, vPosition.X, vPosition.Y, 3))
                    {
                        RepeatMove(this, npc, vPosition.X, vPosition.Y, vPosition.Z);
                        return;
                    }

                    if (MoveToForward)
                    {
                        if (MoveStepIndex == MovePath.Count - 1)
                        {
                            _log.Warn("we are at the end point.");
                            //StopMove(npc);
                            PauseMove(npc);
                            MoveToForward = false; //turn back
                            MoveStepIndex--;
                            _log.Warn("walk to #" + MoveStepIndex);
                            s = MovePath[MoveStepIndex];
                            vPosition.X = ExtractValue(s, 1);
                            vPosition.Y = ExtractValue(s, 2);
                            vPosition.Z = ExtractValue(s, 3);
                            RepeatMove(this, npc, vPosition.X, vPosition.Y, vPosition.Z, 20000);
                            return;
                        }

                        MoveStepIndex++;
                        _log.Warn("we have reached checkpoint go on...");
                    }
                    else
                    {
                        if (MoveStepIndex > 0)
                        {
                            MoveStepIndex--;
                            _log.Warn("we reached checkpoint go further ...");
                        }
                        else
                        {
                            _log.Warn("we are at the starting point.");
                            //StopMove(npc);
                            PauseMove(npc);
                            MoveToForward = true; //turn back
                            MoveStepIndex++;
                            _log.Warn("walk to #" + MoveStepIndex);
                            s = MovePath[MoveStepIndex];
                            vPosition.X = ExtractValue(s, 1);
                            vPosition.Y = ExtractValue(s, 2);
                            vPosition.Z = ExtractValue(s, 3);
                            RepeatMove(this, npc, vPosition.X, vPosition.Y, vPosition.Z, 20000);
                            return;
                        }
                    }

                    _log.Warn("walk to #" + MoveStepIndex);
                    s = MovePath[MoveStepIndex];
                    vPosition.X = ExtractValue(s, 1);
                    vPosition.Y = ExtractValue(s, 2);
                    vPosition.Z = ExtractValue(s, 3);
                    RepeatMove(this, npc, vPosition.X, vPosition.Y, vPosition.Z);
                }
                if (TransferPath.Count > 0)
                {
                    var s = TransferPath[MoveStepIndex];
                    vPosition.X = s.X;
                    vPosition.Y = s.Y;
                    vPosition.Z = s.Z;
                    if (!PosInRange(npc, vPosition.X, vPosition.Y, 3))
                    {
                        RepeatMove(this, npc, vPosition.X, vPosition.Y, vPosition.Z);
                        return;
                    }

                    if (MoveToForward)
                    {
                        if (MoveStepIndex == TransferPath.Count - 1)
                        {
                            _log.Warn("we are at the end point.");
                            //StopMove(npc);
                            PauseMove(npc);
                            MoveToForward = false; //turn back
                            MoveStepIndex--;
                            _log.Warn("walk to #" + MoveStepIndex);
                            s = TransferPath[MoveStepIndex];
                            vPosition.X = s.X;
                            vPosition.Y = s.Y;
                            vPosition.Z = s.Z;
                            RepeatMove(this, npc, vPosition.X, vPosition.Y, vPosition.Z, 20000);
                            return;
                        }

                        MoveStepIndex++;
                        _log.Warn("we have reached checkpoint go on...");
                    }
                    else
                    {
                        if (MoveStepIndex > 0)
                        {
                            MoveStepIndex--;
                            _log.Warn("we reached checkpoint go further ...");
                        }
                        else
                        {
                            _log.Warn("we are at the starting point.");
                            //StopMove(npc);
                            PauseMove(npc);
                            MoveToForward = true; //turn back
                            MoveStepIndex++;
                            _log.Warn("walk to #" + MoveStepIndex);
                            s = TransferPath[MoveStepIndex];
                            vPosition.X = s.X;
                            vPosition.Y = s.Y;
                            vPosition.Z = s.Z;
                            RepeatMove(this, npc, vPosition.X, vPosition.Y, vPosition.Z, 20000);
                            return;
                        }
                    }

                    _log.Warn("walk to #" + MoveStepIndex);
                    s = TransferPath[MoveStepIndex];
                    vPosition.X = s.X;
                    vPosition.Y = s.Y;
                    vPosition.Z = s.Z;
                    RepeatMove(this, npc, vPosition.X, vPosition.Y, vPosition.Z);
                }
            }
            else if (unit is Transfer transfer)
            {
                if (!MoveToPathEnabled || transfer.Position == null || !transfer.IsInPatrol)
                {
                    _log.Warn("Move disabled");
                    StopMove(transfer);
                    return;
                }
                if (MovePath.Count > 0)
                {
                    var s = MovePath[MoveStepIndex];
                    vPosition.X = ExtractValue(s, 1);
                    vPosition.Y = ExtractValue(s, 2);
                    vPosition.Z = ExtractValue(s, 3);
                    if (!PosInRange(transfer, vPosition.X, vPosition.Y, 3))
                    {
                        RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z);
                        return;
                    }

                    if (MoveToForward)
                    {
                        if (MoveStepIndex == MovePath.Count - 1)
                        {
                            _log.Warn("we are at the end point.");
                            //StopMove(npc);
                            PauseMove(transfer);
                            MoveToForward = false; //turn back
                            MoveStepIndex--;
                            _log.Warn("walk to #" + MoveStepIndex);
                            s = MovePath[MoveStepIndex];
                            vPosition.X = ExtractValue(s, 1);
                            vPosition.Y = ExtractValue(s, 2);
                            vPosition.Z = ExtractValue(s, 3);
                            RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z, transfer.Template.WaitTime * 1000);
                            return;
                        }

                        MoveStepIndex++;
                        _log.Warn("we reached checkpoint go further...");
                    }
                    else
                    {
                        if (MoveStepIndex > 0)
                        {
                            MoveStepIndex--;
                            _log.Warn("we reached checkpoint go further ...");
                        }
                        else
                        {
                            _log.Warn("we are at the starting point.");
                            //StopMove(npc);
                            PauseMove(transfer);
                            MoveToForward = true; //turn back
                            MoveStepIndex++;
                            _log.Warn("walk to #" + MoveStepIndex);
                            s = MovePath[MoveStepIndex];
                            vPosition.X = ExtractValue(s, 1);
                            vPosition.Y = ExtractValue(s, 2);
                            vPosition.Z = ExtractValue(s, 3);
                            RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z, transfer.Template.WaitTime * 1000);
                            return;
                        }
                    }

                    _log.Warn("walk to #" + MoveStepIndex);
                    s = MovePath[MoveStepIndex];
                    vPosition.X = ExtractValue(s, 1);
                    vPosition.Y = ExtractValue(s, 2);
                    vPosition.Z = ExtractValue(s, 3);
                    RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z);
                }
                if (TransferPath.Count > 0)
                {
                    var s = TransferPath[MoveStepIndex];
                    vPosition.X = s.X;
                    vPosition.Y = s.Y;
                    vPosition.Z = s.Z;
                    //var carriage = TransferManager.Instance.GetTransferTemplate(transfer.TemplateId);
                    if (PosInRange(transfer, vPosition.X, vPosition.Y, 50))
                    {
                        if (MoveToForward)
                        {
                            /*
                             проходим по очереди все участки пути из списка,
                             с начала каждого пути, начиная с середины это пути в обратную сторону
                            */
                            if (MoveStepIndex == TransferPath.Count - 1)
                            {
                                // участок пути закончился
                                Steering++; // укажем на следующий путь
                                if (Steering == AllRoutes[transfer.TemplateId].Count)
                                {
                                    transfer.SetPosition(transfer.Position.X, transfer.Position.Y, transfer.Position.Z, transfer.Position.RotationX, transfer.Position.RotationY, transfer.Position.RotationZ);
                                    // закончились дороги, нужно начать сначала
                                    Steering = 0; // укажем на начальный путь
                                    // нужно разворачиваться
                                    _log.Warn("we are at the end point.");
                                    PauseMove(transfer);
                                    // продолжим путь после паузы назад
                                    LoadTransferPath2(transfer.TemplateId, Steering); // загрузим путь в TransferPath
                                    MoveStepIndex = 0;
                                    _log.Warn("next path #" + Steering);
                                    _log.Warn("walk to #" + MoveStepIndex);
                                    _log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                                    s = TransferPath[MoveStepIndex];
                                    vPosition.X = s.X;
                                    vPosition.Y = s.Y;
                                    vPosition.Z = s.Z;
                                    // здесь будет непосредственно пауза между участками дороги, если она есть в базе данных
                                    RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z, transfer.Template.TransferPaths[Steering].WaitTimeStart * 1000);
                                }
                                //else if (Steering == AllRoutes[transfer.TemplateId].Count / 2)
                                //{
                                //    transfer.SetPosition(transfer.Position.X, transfer.Position.Y, transfer.Position.Z, transfer.Position.RotationX, transfer.Position.RotationY, transfer.Position.RotationZ);
                                //    // достигли середины списка, нужно разворачиваться
                                //    _log.Warn("we are at the end point.");
                                //    PauseMove(transfer);
                                //    // продолжим путь после паузы назад
                                //    //LoadTransferPath(Steering); // загрузим путь в TransferPath
                                //    LoadTransferPath2(transfer.TemplateId, Steering); // загрузим путь в TransferPath
                                //    MoveStepIndex = 0;
                                //    _log.Warn("next path #" + Steering);
                                //    _log.Warn("walk to #" + MoveStepIndex);
                                //    _log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                                //    s = TransferPath[MoveStepIndex];
                                //    vPosition.X = s.X;
                                //    vPosition.Y = s.Y;
                                //    vPosition.Z = s.Z;
                                //    // здесь непосредственно пауза
                                //    RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z, transfer.Template.WaitTime * 1000);
                                //}
                                else
                                {
                                    transfer.SetPosition(transfer.Position.X, transfer.Position.Y, transfer.Position.Z, transfer.Position.RotationX, transfer.Position.RotationY, transfer.Position.RotationZ);
                                    // продолжим путь
                                    //LoadTransferPath(Steering); // загрузим путь в TransferPath
                                    LoadTransferPath2(transfer.TemplateId, Steering); // загрузим путь в TransferPath
                                    MoveStepIndex = 0;
                                    _log.Warn("path #" + Steering);
                                    _log.Warn("walk to #" + MoveStepIndex);
                                    _log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                                    s = TransferPath[MoveStepIndex];
                                    vPosition.X = s.X;
                                    vPosition.Y = s.Y;
                                    vPosition.Z = s.Z;
                                    // здесь нет паузы
                                    RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z);
                                }
                            }
                            else
                            {
                                transfer.SetPosition(transfer.Position.X, transfer.Position.Y, transfer.Position.Z, transfer.Position.RotationX, transfer.Position.RotationY, transfer.Position.RotationZ);

                                MoveStepIndex++;
                                _log.Warn("we reached checkpoint go further...");
                                _log.Warn("path #" + Steering);
                                _log.Warn("walk to #" + MoveStepIndex);
                                _log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                                s = TransferPath[MoveStepIndex];
                                vPosition.X = s.X;
                                vPosition.Y = s.Y;
                                vPosition.Z = s.Z;
                                // здесь нет паузы
                                RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z);
                            }
                        }
                        else
                        {
                            //TODO здесь всё тоже самое, но с конца пути
                            //TODO здесь проходим все пути из списка дорог по очереди, с конца каждого пути, начиная с середины это пути в обратную сторону
                            if (MoveStepIndex == 0)
                            {
                                _log.Warn("we are at the begin point.");
                                // путь закончился
                                Steering--; // укажем на предыдущий путь
                                if (Steering < 0)
                                {
                                    transfer.SetPosition(transfer.Position.X, transfer.Position.Y, transfer.Position.Z, transfer.Position.RotationX, transfer.Position.RotationY, transfer.Position.RotationZ);
                                    // закончились дороги, нужно начать с конца
                                    Steering = AllRoutes[transfer.TemplateId].Count - 1; // укажем на самый последний в списке путь
                                    PauseMove(transfer); // продолжим путь после паузы назад
                                    //LoadTransferPath(Steering); // загрузим путь в TransferPath
                                    LoadTransferPath2(transfer.TemplateId, Steering); // загрузим путь в TransferPath
                                    MoveStepIndex = TransferPath.Count - 1; // укажем на последнюю точку в пути
                                    _log.Warn("next path #" + Steering);
                                    _log.Warn("walk to #" + MoveStepIndex);
                                    _log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                                    s = TransferPath[MoveStepIndex];
                                    vPosition.X = s.X;
                                    vPosition.Y = s.Y;
                                    vPosition.Z = s.Z;
                                    // здесь непосредственно пауза
                                    RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z, transfer.Template.WaitTime * 1000);
                                }
                                else if (Steering == AllRoutes[transfer.TemplateId].Count / 2 - 1)
                                {
                                    transfer.SetPosition(transfer.Position.X, transfer.Position.Y, transfer.Position.Z, transfer.Position.RotationX, transfer.Position.RotationY, transfer.Position.RotationZ);
                                    // достигли середины списка, нужно разворачиваться
                                    _log.Warn("have reached the middle of the list, we need to turn around...");
                                    PauseMove(transfer); // продолжим путь после паузы назад
                                    //LoadTransferPath(Steering); // загрузим путь в TransferPath
                                    LoadTransferPath2(transfer.TemplateId, Steering); // загрузим путь в TransferPath
                                    MoveStepIndex = TransferPath.Count - 1; // укажем на последнюю точку в пути
                                    _log.Warn("next path #" + Steering);
                                    _log.Warn("walk to #" + MoveStepIndex);
                                    _log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                                    s = TransferPath[MoveStepIndex];
                                    vPosition.X = s.X;
                                    vPosition.Y = s.Y;
                                    vPosition.Z = s.Z;
                                    // здесь непосредственно пауза
                                    RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z, transfer.Template.WaitTime * 1000);
                                }
                                else
                                {
                                    transfer.SetPosition(transfer.Position.X, transfer.Position.Y, transfer.Position.Z, transfer.Position.RotationX, transfer.Position.RotationY, transfer.Position.RotationZ);
                                    // продолжим путь
                                    //LoadTransferPath(Steering); // загрузим путь в TransferPath
                                    LoadTransferPath2(transfer.TemplateId, Steering); // загрузим путь в TransferPath
                                    MoveStepIndex = TransferPath.Count - 1; // укажем на последнюю точку в пути
                                    _log.Warn("path #" + Steering);
                                    _log.Warn("walk to #" + MoveStepIndex);
                                    _log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                                    s = TransferPath[MoveStepIndex];
                                    vPosition.X = s.X;
                                    vPosition.Y = s.Y;
                                    vPosition.Z = s.Z;
                                    // здесь нет паузы
                                    RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z);
                                }
                            }
                            else
                            {
                                transfer.SetPosition(transfer.Position.X, transfer.Position.Y, transfer.Position.Z, transfer.Position.RotationX, transfer.Position.RotationY, transfer.Position.RotationZ);
                                MoveStepIndex--;
                                _log.Warn("we reached checkpoint go further...");
                                _log.Warn("path #" + Steering);
                                _log.Warn("walk to #" + MoveStepIndex);
                                _log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                                s = TransferPath[MoveStepIndex];
                                vPosition.X = s.X;
                                vPosition.Y = s.Y;
                                vPosition.Z = s.Z;
                                // здесь нет паузы
                                RepeatMove(this, transfer, vPosition.X, vPosition.Y, vPosition.Z);
                            }
                        }
                    }
                    else
                    {
                        transfer.SetPosition(transfer.Position.X, transfer.Position.Y, transfer.Position.Z, transfer.Position.RotationX, transfer.Position.RotationY, transfer.Position.RotationZ);
                        _log.Warn("path #" + Steering);
                        _log.Warn("checkpoint #" + MoveStepIndex);
                        _log.Warn("x:=" + vPosition.X + " y:=" + vPosition.Y + " z:=" + vPosition.Z);
                        _log.Warn("to any point is very far, we stop.");
                    }
                }
            }
        }

        public void Init(Unit unit) // Called when the script is start
        {
            //switch (unit)
            //{
            //    //case Character ch:
            //    //    chrctr = ch;
            //    //    ch.Position = new Point();
            //    //    break;
            //    case Npc np:
            //        npc = np;
            //        //np.Position = new Point();
            //        break;
            //    case Transfer tr:
            //        trnsfr = tr;
            //        //vDistance = new Vector3();
            //        //vPosition = new Vector3();
            //        break;
            //}

            RecordPath = new List<string>();
            MovePath = new List<string>();
            TransferPath = new List<Point>();
        }

        public void ReadPath() // Called when the script is start
        {
            try
            {
                MovePath = new List<string>();
                MovePath = File.ReadLines(GetMoveFileName()).ToList();
                _log.Info("Loading {0} transfer_path...", GetMoveFileName());
            }
            catch (Exception e)
            {
                _log.Warn("Error in read MovePath: {0}", e);
                //StopMove(npc);
            }
            //try
            //{
            //    RecordPath = new List<string>();
            //    //RecordPath = File.ReadLines(GetMoveFileName()).ToList();
            //}
            //catch (Exception e)
            //{
            //    _log.Warn("Error in read RecordPath: {0}", e);
            //    //StopMove(npc);
            //}
        }

        public List<Point> LoadPath(string namePath) //Вызывается при включении скрипта
        {
            _log.Info("Transfer: Loading {0} transfer_path...", namePath);
            TransferPath = TransferManager.Instance.GetTransferPath(namePath);
            return TransferPath;
        }

        public void ReadPath(string namePath) //Вызывается при включении скрипта
        {
            _log.Info("Transfer: Reading {0} transfer_path...", namePath);
            TransferPath = TransferManager.Instance.GetTransferPath(namePath);
        }

        public void AddPath(string namePath) //Добавить продолжение маршрута
        {
            _log.Info("Transfer: Adding {0} transfer_path...", namePath);
            TransferPath.AddRange(TransferManager.Instance.GetTransferPath(namePath));
        }

        public override void Execute(BaseUnit unit)
        {
            //NextPathOrPointInPath(npc);
            OnMove(unit);
        }

        public override void Execute(Transfer transfer)
        {
            //NextPathOrPointInPath(transfer);
            OnMove(transfer);
        }
        public override void Execute(Gimmick gimmick)
        {
            throw new NotImplementedException();
        }
    }
}
