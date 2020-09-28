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
using AAEmu.Game.Models.Game.World;
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
        public Character character;
        public Npc npc;
        public Transfer transfer;

        public bool AbandonTo { get; set; } = false; // для прерывания repeat()
        // +++
        private float _maxVelocityForward;
        private float _maxVelocityBackward;
        private float _velAccel;
        private float _angVel;
        private int Steering;
        private float diffX;
        private float diffY;
        private float diffZ;
        //
        public float DeltaTime { get; set; } = 0.05f;
        public double _angleTmp;
        public Vector3 _vPosition;
        public Vector3 _vTarget;
        public Vector3 _vDistance;
        public Vector3 _vVelocity;
        public double rad;
        // movement data
        public List<string> MovePath;     //  the data we're going to be moving on at the moment
        public List<Point> TransferPath;  // path from client file
        public List<string> RecordPath;   // data to write the path
        public Dictionary<int, List<Point>> Routes; // Steering, TransferPath
        public Dictionary<uint, Dictionary<int, List<Point>>> _allRoutes; // templateId, Steering, TransferPath
        public Dictionary<uint, Dictionary<int, string>> _allRouteNames; // templateId, Steering, TransferPath
        // +++
        public int PointsCount { get; set; }              // number of points in the process of recording the path
        public bool SavePathEnabled { get; set; }         // flag, path recording
        public bool MoveToPathEnabled { get; set; }       // flag, road traffic
        public bool MoveToForward { get; set; }           // movement direction true -> forward, true -> back
        public bool RunningMode { get; set; } = false;    // movement mode true -> run, true -> walk
        public bool Move { get; set; } = false;           // movement mode true -> moving to the point #, false -> get next point #
        public int MoveStepIndex { get; set; }            // current checkpoint (where are we running now)

        private float _oldX, _oldY, _oldZ;
        //*******************************************************
        public string RecordFilesPath = @"./Data/Path/"; // path where our files are stored
        public string RecordFileExt = @".path";          // default extension
        public string MoveFilesPath = @"./Data/Path/";   // path where our files are stored
        public string MoveFileExt = @".path";            // default extension
        public string MoveFileName = "";                 // default name
        private float _tempMovingDistance;
        private float _rangeToCheckPoint = 0.5f; // distance to checkpoint at which it is considered that we have reached it
        private int _moveTrigerDelay = 1000;     // triggering the timer for movement 1 sec

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
                unit.WorldPos = new WorldPos();
                _velAccel = velAcceleration; //per s
                _maxVelocityForward = velocityForward; // 9.6f;
                _maxVelocityBackward = velocityBackward;
                _velAccel = velAcceleration;
                _angVel = angVelocity;
                Steering = 0;
                //var linInertia = 0.3f;    //per s   // TODO Move to the upper motion control module
                //var linDeaccelInertia = 0.1f;  //per s   // TODO Move to the upper motion control module
                //var maxVelBackward = -2.0f; //per s
                //var diffX = 0f;
                //var diffY = 0f;
                //var diffZ = 0f;
            }
            Init(unit);
        }
        //***************************************************************
        //public override void Execute(Actor unit)
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
                    _vPosition.X = ExtractValue(s, 1);
                    _vPosition.Y = ExtractValue(s, 2);
                    _vPosition.Z = ExtractValue(s, 3);

                    //s_log.Warn(s + " #" + i + " x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);

                    m = Delta(_vPosition.X, _vPosition.Y, npc.Position.X, npc.Position.Y);

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
                _vTarget = new Vector3(transfer.Position.X, transfer.Position.Y, transfer.Position.Z);
                for (var i = 0; i < pointsList.Count; i++)
                {
                    s = pointsList[i];
                    _vPosition = new Vector3(ExtractValue(s, 1), ExtractValue(s, 2), ExtractValue(s, 3));

                    //s_log.Warn(s + " x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);

                    delta = MathUtil.GetDistance(_vTarget, _vPosition);

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
            foreach (var (id, routes) in _allRoutes)
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
                //s_log.Warn("no route data.");
                return -1;
            }
            if (unit is Transfer transfer)
            {
                float delta;
                var minDist = 0f;
                _vTarget = new Vector3(transfer.Position.X, transfer.Position.Y, transfer.Position.Z);
                for (var i = 0; i < pointsList.Count; i++)
                {
                    _vPosition = new Vector3(pointsList[i].X, pointsList[i].Y, pointsList[i].Z);

                    //s_log.Warn("#" + i + " x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);

                    delta = MathUtil.GetDistance(_vTarget, _vPosition);

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
                //s_log.Warn("no route data.");
                return -1;
            }
            if (unit is Npc npc)
            {
                int m, minDist;
                minDist = -1;
                for (var i = 0; i < pointsList.Count; i++)
                {
                    _vPosition.X = pointsList[i].X;
                    _vPosition.Y = pointsList[i].Y;
                    _vPosition.Z = pointsList[i].Z;

                    //s_log.Warn("#" + i + " x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);

                    m = Delta(_vPosition.X, _vPosition.Y, npc.Position.X, npc.Position.Y);

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
                _vTarget = new Vector3(transfer.Position.X, transfer.Position.Y, transfer.Position.Z);
                for (var i = 0; i < pointsList.Count; i++)
                {
                    _vPosition = new Vector3(pointsList[i].X, pointsList[i].Y, pointsList[i].Z);

                    //s_log.Warn("#" + i + " x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);

                    delta = MathUtil.GetDistance(_vTarget, _vPosition);
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
                //s_log.Warn("while following the route, recording is not possible.");
                return;
            }
            RecordPath.Clear();
            PointsCount = 0;
            //s_log.Warn("route recording started ...");
            SavePathEnabled = true;
            RepeatTo(ch, _moveTrigerDelay, sim);
        }
        //***************************************************************
        public void Record(Simulation sim, Character ch)
        {
            //if (!SavePathEnabled) { return; }
            var s = "|" + ch.Position.X + "|" + ch.Position.Y + "|" + ch.Position.Z + "|";
            RecordPath.Add(s);
            PointsCount++;
            //s_log.Warn("added checkpoint # {0}", PointsCount);
            RepeatTo(ch, _moveTrigerDelay, sim);
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
            //s_log.Warn("Route recording completed.");
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
            TransferPath = _allRoutes[id][index];
        }
        //***************************************************************
        public void LoadAllPath(Point position)
        {
            Routes = TransferManager.Instance.GetAllTransferPath(position);

        }
        //***************************************************************
        public void LoadAllPath2(uint templateId, Point position, byte worldId = 1)
        {
            (_allRoutes, _allRouteNames) = TransferManager.Instance.GetAllTransferPath2(templateId);
        }
        //***************************************************************
        public void ParseMoveClient(Unit unit)
        {
            if (!SavePathEnabled) { return; }
            if (unit is Npc npc)
            {
                _vPosition.X = npc.Position.X;
                _vPosition.Y = npc.Position.Y;
                _vPosition.Z = npc.Position.Z;
            }
            else if (unit is Transfer transfer)
            {
                _vPosition.X = transfer.Position.X;
                _vPosition.Y = transfer.Position.Y;
                _vPosition.Z = transfer.Position.Z;
            }
            var s = "|" + _vPosition.X + "|" + _vPosition.Y + "|" + _vPosition.Z + "|";
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
                    //s_log.Warn("the route is stopped.");
                    StopMove(transfer);
                    return;
                }

                // presumably the path is already registered in MovePath
                //s_log.Warn("trying to get on the road...");
                // first go to the closest checkpoint
                //var i = GetMinCheckPoint(transfer, TransferPath);
                var (msi, idx) = GetMinCheckPointFromRoutes(transfer);
                if (msi < 0)
                {
                    //s_log.Warn("no checkpoint found.");
                    StopMove(transfer);
                    return;
                }
                Steering = idx; // индекс нужного "пути" в списке дорог
                //LoadTransferPath(idx);   // загружаем "путь" в TransferPath
                LoadTransferPath2(transfer.TemplateId, idx);   // загружаем "путь" в TransferPath
                //s_log.Warn("found index routes # " + idx + " nearest checkpoint # " + msi + " walk there ...");
                //s_log.Warn("index #" + idx);
                //s_log.Warn("checkpoint #" + msi);
                //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);

                MoveToPathEnabled = true;
                MoveStepIndex = msi; // текущая точка пути
                var s = TransferPath[MoveStepIndex]; // текущий "путь" с текущим индексом точки куда идти
                _vPosition.X = s.X;
                _vPosition.Y = s.Y;
                _vPosition.Z = s.Z;

                if (Math.Abs(_oldX - _vPosition.X) > Tolerance && Math.Abs(_oldY - _vPosition.Y) > Tolerance && Math.Abs(_oldZ - _vPosition.Z) > Tolerance)
                {
                    _oldX = _vPosition.X;
                    _oldY = _vPosition.Y;
                    _oldZ = _vPosition.Z;
                }
            }
            RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z);
        }
        public void GoToPathFromRoutes2(Transfer transfer, bool toForward = true)
        {
            if (_allRoutes[transfer.TemplateId].Count > 0) // имеются участки пути для дорог?
            {
                MoveToPathEnabled = !MoveToPathEnabled;
                MoveToForward = toForward;
                if (!MoveToPathEnabled || transfer.Position == null || !transfer.IsInPatrol)
                {
                    //s_log.Warn("the route is stopped.");
                    StopMove(transfer);
                    return;
                }

                // presumably the path is already registered in MovePath
                //s_log.Warn("trying to get on the road...");
                // first go to the closest checkpoint
                //var i = GetMinCheckPoint(transfer, TransferPath);
                var (pointIndex, routeIndex) = GetMinCheckPointFromRoutes2(transfer);
                if (pointIndex == -1)
                {
                    //s_log.Warn("no checkpoint found.");
                    StopMove(transfer);
                    return;
                }
                Steering = routeIndex; // индекс нужного участка пути в списке дорог
                LoadTransferPath2(transfer.TemplateId, routeIndex);   // загружаем участок пути в TransferPath
                //s_log.Warn("found index routes # " + routeIndex + " nearest checkpoint # " + pointIndex + " walk there ...");
                //s_log.Warn("index #" + routeIndex);
                //s_log.Warn("checkpoint #" + pointIndex);
                //s_log.Warn("x={0}, y={1}, z={2}, rotZ={3}, zoneId={4}", transfer.Position.X, transfer.Position.Y, transfer.Position.Z, transfer.Position.RotationZ, transfer.Position.ZoneId);

                MoveToPathEnabled = true;
                MoveStepIndex = pointIndex; // текущая точка пути
                var s = TransferPath[MoveStepIndex]; // текущий участок пути с текущим индексом точки куда идти
                _vPosition.X = s.X;
                _vPosition.Y = s.Y;
                _vPosition.Z = s.Z;

                if (Math.Abs(_oldX - _vPosition.X) > Tolerance && Math.Abs(_oldY - _vPosition.Y) > Tolerance && Math.Abs(_oldZ - _vPosition.Z) > Tolerance)
                {
                    _oldX = _vPosition.X;
                    _oldY = _vPosition.Y;
                    _oldZ = _vPosition.Z;
                }
            }
            RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z);
        }
        public void GoToPath(Unit unit, bool toForward)
        {
            if (unit is Npc npc)
            {
                if (MovePath.Count > 0)
                {
                    MoveToPathEnabled = !MoveToPathEnabled;
                    MoveToForward = toForward;
                    if (!MoveToPathEnabled)
                    {
                        //s_log.Warn("the route is stopped.");
                        StopMove(npc);
                        return;
                    }

                    // presumably the path is already registered in MovePath
                    //s_log.Warn("trying to get on the path ...");
                    // first go to the closest checkpoint
                    var i = GetMinCheckPoint(npc, MovePath);
                    if (i < 0)
                    {
                        //s_log.Warn("checkpoint not found.");
                        StopMove(npc);
                        return;
                    }

                    //s_log.Warn("found nearest checkpoint # " + i + " run there ...");
                    MoveToPathEnabled = true;
                    MoveStepIndex = i;
                    //s_log.Warn("checkpoint #" + i);
                    var s = MovePath[MoveStepIndex];
                    _vPosition.X = ExtractValue(s, 1);
                    _vPosition.Y = ExtractValue(s, 2);
                    _vPosition.Z = ExtractValue(s, 3);

                    if (Math.Abs(_oldX - _vPosition.X) > Tolerance && Math.Abs(_oldY - _vPosition.Y) > Tolerance && Math.Abs(_oldZ - _vPosition.Z) > Tolerance)
                    {
                        _oldX = _vPosition.X;
                        _oldY = _vPosition.Y;
                        _oldZ = _vPosition.Z;
                    }
                }
                if (TransferPath.Count > 0)
                {
                    MoveToPathEnabled = !MoveToPathEnabled;
                    MoveToForward = toForward;
                    if (!MoveToPathEnabled)
                    {
                        //s_log.Warn("the route is stopped.");
                        StopMove(npc);
                        return;
                    }

                    // presumably the path is already registered in MovePath
                    //s_log.Warn("trying to get on the path ...");
                    // first go to the closest checkpoint
                    var i = GetMinCheckPoint(npc, TransferPath);
                    if (i < 0)
                    {
                        //s_log.Warn("checkpoint not found.");
                        StopMove(npc);
                        return;
                    }

                    //s_log.Warn("found nearest checkpoint # " + i + " run there ...");
                    //s_log.Warn(" #" + i + "x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                    MoveToPathEnabled = true;
                    MoveStepIndex = i;
                    //s_log.Warn("checkpoint #" + i);
                    //s_log.Warn(" #" + i + "x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                    var s = TransferPath[MoveStepIndex];
                    _vPosition.X = s.X;
                    _vPosition.Y = s.Y;
                    _vPosition.Z = s.Z;

                    if (Math.Abs(_oldX - _vPosition.X) > Tolerance && Math.Abs(_oldY - _vPosition.Y) > Tolerance && Math.Abs(_oldZ - _vPosition.Z) > Tolerance)
                    {
                        _oldX = _vPosition.X;
                        _oldY = _vPosition.Y;
                        _oldZ = _vPosition.Z;
                    }
                }
                RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z);
            }
            else if (unit is Transfer transfer)
            {
                if (MovePath.Count > 0)
                {
                    MoveToPathEnabled = !MoveToPathEnabled;
                    MoveToForward = toForward;
                    if (!MoveToPathEnabled)
                    {
                        //s_log.Warn("the route has been stopped.");
                        StopMove(transfer);
                        return;
                    }

                    // presumably the path is already registered in MovePath
                    //s_log.Warn("trying to get on the road...");
                    // first go to the closest checkpoint
                    var i = GetMinCheckPoint(transfer, MovePath);
                    if (i < 0)
                    {
                        //s_log.Warn("no checkpoint found.");
                        StopMove(transfer);
                        return;
                    }

                    //s_log.Warn("found nearest checkpoint # " + i + " walk there ...");
                    MoveToPathEnabled = true;
                    MoveStepIndex = i;
                    //s_log.Warn("checkpoint #" + i);
                    var s = MovePath[MoveStepIndex];
                    _vPosition.X = ExtractValue(s, 1);
                    _vPosition.Y = ExtractValue(s, 2);
                    _vPosition.Z = ExtractValue(s, 3);

                    if (Math.Abs(_oldX - _vPosition.X) > Tolerance && Math.Abs(_oldY - _vPosition.Y) > Tolerance && Math.Abs(_oldZ - _vPosition.Z) > Tolerance)
                    {
                        _oldX = _vPosition.X;
                        _oldY = _vPosition.Y;
                        _oldZ = _vPosition.Z;
                    }
                }
                if (TransferPath.Count > 0)
                {
                    MoveToPathEnabled = !MoveToPathEnabled;
                    MoveToForward = toForward;
                    if (!MoveToPathEnabled || transfer.Position == null || !transfer.IsInPatrol)
                    {
                        //s_log.Warn("the route is stopped.");
                        StopMove(transfer);
                        return;
                    }

                    // presumably the path is already registered in MovePath
                    //s_log.Warn("trying to get on the road...");
                    // first go to the closest checkpoint
                    var i = GetMinCheckPoint(transfer, TransferPath);
                    if (i < 0)
                    {
                        //s_log.Warn("no checkpoint found.");
                        StopMove(transfer);
                        return;
                    }

                    //s_log.Warn("found nearest checkpoint # " + i + " walk there ...");
                    //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                    MoveToPathEnabled = true;
                    MoveStepIndex = i;
                    //s_log.Warn("checkpoint #" + i);
                    //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                    var s = TransferPath[MoveStepIndex];
                    _vPosition.X = s.X;
                    _vPosition.Y = s.Y;
                    _vPosition.Z = s.Z;

                    if (Math.Abs(_oldX - _vPosition.X) > Tolerance && Math.Abs(_oldY - _vPosition.Y) > Tolerance && Math.Abs(_oldZ - _vPosition.Z) > Tolerance)
                    {
                        _oldX = _vPosition.X;
                        _oldY = _vPosition.Y;
                        _oldZ = _vPosition.Z;
                    }
                }
                RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z, transfer.Template.TransferPaths[Steering].WaitTimeStart * 1000);
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
                var maxXyz = Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Abs(z));

                if (RunningMode)
                {
                    MovingDistance = 0.5f;
                }
                else
                {
                    MovingDistance = 0.25f;
                }


                if (Math.Abs(x) > _rangeToCheckPoint)
                {
                    if (Math.Abs(maxXyz - Math.Abs(x)) > Tolerance)
                    {
                        _tempMovingDistance = Math.Abs(x) / (maxXyz / MovingDistance);
                        _tempMovingDistance = Math.Min(_tempMovingDistance, MovingDistance);
                    }
                    else
                    {
                        _tempMovingDistance = MovingDistance;
                    }

                    if (x < 0)
                    {
                        npc.Position.X += _tempMovingDistance;
                    }
                    else
                    {
                        npc.Position.X -= _tempMovingDistance;
                    }
                    if (Math.Abs(x) < _tempMovingDistance)
                    {
                        npc.Position.X = _vPosition.X;
                    }
                    move = true;
                }
                if (Math.Abs(y) > _rangeToCheckPoint)
                {
                    if (Math.Abs(maxXyz - Math.Abs(y)) > Tolerance)
                    {
                        _tempMovingDistance = Math.Abs(y) / (maxXyz / MovingDistance);
                        _tempMovingDistance = Math.Min(_tempMovingDistance, MovingDistance);
                    }
                    else
                    {
                        _tempMovingDistance = MovingDistance;
                    }
                    if (y < 0)
                    {
                        npc.Position.Y += _tempMovingDistance;
                    }
                    else
                    {
                        npc.Position.Y -= _tempMovingDistance;
                    }
                    if (Math.Abs(y) < _tempMovingDistance)
                    {
                        npc.Position.Y = _vPosition.Y;
                    }
                    move = true;
                }
                if (Math.Abs(z) > _rangeToCheckPoint)
                {
                    if (Math.Abs(maxXyz - Math.Abs(z)) > Tolerance)
                    {
                        _tempMovingDistance = Math.Abs(z) / (maxXyz / MovingDistance);
                        _tempMovingDistance = Math.Min(_tempMovingDistance, MovingDistance);
                    }
                    else
                    {
                        _tempMovingDistance = MovingDistance;
                    }
                    if (z < 0)
                    {
                        npc.Position.Z += _tempMovingDistance;
                    }
                    else
                    {
                        npc.Position.Z -= _tempMovingDistance;
                    }
                    if (Math.Abs(z) < _tempMovingDistance)
                    {
                        npc.Position.Z = _vPosition.Z;
                    }
                    move = true;
                }
                // simulation unit. return the unitMovement object
                moveType = (ActorData)UnitMovement.GetType(UnitMovementType.Actor);
                // Change the NPC coordinates
                //moveType.X = npc.Position.X;
                //moveType.Y = npc.Position.Y;
                //moveType.Z = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;
                //// looks in the direction of movement
                ////-----------------------взгляд_NPC_будет(движение_откуда->движение_куда)
                //angle = MathUtil.CalculateAngleFrom(npc.Position.X, npc.Position.Y, vPosition.X, vPosition.Y);
                //var rotZ = MathUtil.ConvertDegreeToDirection(angle);
                //moveType.RotationX = 0;
                //moveType.RotationY = 0;
                //moveType.RotationZ = rotZ;
                //if (RunningMode)
                //{
                //    moveType.Flags = 4;      // 5-walk, 4-run, 3-stand still
                //}
                //else
                //{
                //    moveType.Flags = 5;      // 5-walk, 4-run, 3-stand still
                //}
                //moveType.DeltaMovement = new sbyte[3];
                //moveType.DeltaMovement[0] = 0;
                //moveType.DeltaMovement[1] = 127;
                //moveType.DeltaMovement[2] = 0;

                var tmpZ = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;
                moveType.WorldPos = new WorldPos(npc.Pos.X, npc.Pos.Y, tmpZ);

                var direction = new Vector3();
                if (_vDistance != Vector3.Zero)
                {
                    direction = Vector3.Normalize(_vDistance);
                }

                var rotation = (float)Math.Atan2(direction.Y, direction.X);
                moveType.Rot = Quaternion.CreateFromAxisAngle(direction, rotation);

                if (RunningMode)
                {
                    moveType.Flags = 4;      // 5-walk, 4-run, 3-stand still
                }
                else
                {
                    moveType.Flags = 5;      // 5-walk, 4-run, 3-stand still
                }

                moveType.DeltaMovement = direction;


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

                // create a pointer where we are going
                //var spwnFlag = SpawnFlag(vTarget);

                vTarget = new Vector3(targetX, targetY, targetZ);
                vPosition = new Vector3(transfer.Position.X, transfer.Position.Y, transfer.Position.Z);
                // вектор направление на таргет (последовательность аргументов важно, чтобы смотреть на таргет)
                vDistance = vTarget - vPosition; // dx, dy, dz
                // distance to the point where we are moving
                Distance = MathUtil.GetDistance(vTarget, vPosition);

                if (!(Distance > 0))
                {
                    InPatrol = false;
                    Time = 0f;

                    // get next path or point # in current path
                    OnMove2(transfer);
                    return;
                }

                // get current values
                // var deltaTime = (float)(DateTime.Now - GameService.StartTime).TotalSeconds;
                DeltaTime = 0.05f; // temporarily took a constant, later it will be necessary to take the current time

                //var velocityX = vVelocity.X;
                //var velocityY = vVelocity.Y;
                //var velocityZ = vVelocity.Z;
                // accelerate to maximum speed
                transfer.Speed += _velAccel * DeltaTime;
                // check that it is not more than the maximum forward or reverse speed
                transfer.Speed = Math.Clamp(transfer.Speed, _maxVelocityBackward, _maxVelocityForward);

                // скорость движения velociti за период времени DeltaTime
                var velociti = transfer.Speed * DeltaTime;
                // вектор направления необходимо нормализовать
                if (vDistance != Vector3.Zero)
                {
                    vDistance = Vector3.Normalize(vDistance);
                }
                // вектор скорости (т.е. координаты, куда попадем двигаясь со скоростью velociti по направдению direction)
                var diff = vDistance * velociti;
                transfer.Position.X += diff.X;
                transfer.Position.Y += diff.Y;
                transfer.Position.Z += diff.Z;
                if (Math.Abs(vDistance.X) < RangeToCheckPoint) { transfer.Position.X = vTarget.X; }
                if (Math.Abs(vDistance.Y) < RangeToCheckPoint) { transfer.Position.Y = vTarget.Y; }
                if (Math.Abs(vDistance.Z) < RangeToCheckPoint) { transfer.Position.Z = vTarget.Z; }

                //rad = MathUtil.CalculateDirection(vPosition, vTarget);
                //var cosA = (float)Math.Cos(rad);
                //var sinA = (float)Math.Sin(rad);
                //diffX = MovingDistance * cosA;
                //diffY = MovingDistance * sinA;
                //var velX = transfer.Speed * cosA;
                //var velY = transfer.Speed * sinA;

                //if the distance in X to travel is greater than the distance that can be traveled in a period of time
                //if (Math.Abs(vDistance.X) > RangeToCheckPoint)
                //{
                //    transfer.Position.X += diff.X;
                //}
                //else
                //{
                //    transfer.Position.X = vTarget.X;
                //}
                // if the distance in Y to travel is greater than the distance that can be traveled in a period of time
                //if (Math.Abs(vDistance.Y) > RangeToCheckPoint)
                //{
                //    transfer.Position.Y += diff.Y;
                //}
                //else
                //{
                //    transfer.Position.Y = vTarget.Y;
                //}

                //var cosA = (float)Math.Cos(MathUtil.CalculateDirectionZ(vPosition, vTarget));
                //diffZ = MovingDistance * cosA;
                //if (Math.Abs(vDistance.Z) > RangeToCheckPoint)
                //{
                //    transfer.Position.Z += diffZ;
                //    var tempPosZ = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(transfer.Position.ZoneId, vPosition.X, vPosition.Y) : vPosition.Z;
                //    if (transfer.Position.Z < tempPosZ)
                //    {
                //        transfer.Position.Z = tempPosZ;
                //    }
                //}
                //else
                //{
                //    transfer.Position.Z = vTarget.Z;
                //}

                //var velZ = transfer.Speed * cosA;
                //transfer.Velocity = new Vector3(velX, velY, velZ);
                //vVelocity = new Vector3(velX, velY, velZ);

                // looks in the direction of movement
                //_angleTmp = MathUtil.RadianToDegree(rad);
                // slowly turn to the desired angle
                //if (_angleTmp > 0)
                //{
                //    _angle += _angVel;
                //    _angle = (float)Math.Clamp(_angle, 0f, _angleTmp);
                //}
                //else
                //{
                //    _angle -= _angVel;
                //    _angle = (float)Math.Clamp(_angle, _angleTmp, 0f);
                //}
                // var rotZ = MathUtil.ConvertDegreeToDirectionShort(angle);
                //var rotZ = MathUtil.UpdateHeading(angle);
                //transfer.Rot.Z = rotZ;
                //var tmpZ = Helpers.ConvertRotation(transfer.Position.RotationZ);


                //var axis = new Vector3(0f, 0f, 1f);
                //transfer.Rot = Quaternion.CreateFromAxisAngle(axis, (float)_angleTmp);

                //var temp = Quaternion.CreateFromYawPitchRoll((float)_angleTmp, 0 , 0);
                //transfer.Rot = new Quaternion(0f, 0f, temp.Y * 32767f, temp.W * 32767);
                // чтобы не разворачивало назад, если точка последняя
                if (Math.Abs(Distance) < 1.5f && MoveStepIndex == TransferPath.Count - 1)
                {
                    var pp = Steering >= Routes.Count - 1 ? Routes[0][0] : Routes[Steering + 1][0];
                    var vNewTarget = new Vector3(pp.X, pp.Y, pp.Z);
                    Angle = MathUtil.CalculateDirection(vPosition, vNewTarget);
                }
                else
                {
                    Angle = MathUtil.CalculateDirection(vPosition, vTarget);
                }
                transfer.Position.RotationZ = MathUtil.ConvertDegreeToDirection(MathUtil.RadianToDegree(Angle));
                transfer.Rot = new Quaternion(0f, 0f, MathUtil.ConvertToDirection(Angle), 1f);
                transfer.AngVel = new Vector3(0f, 0f, (float)Angle); // сюда записывать дельту, в радианах, угла поворота между начальным вектором и конечным
                //if (transfer.TemplateId == 52)
                //{
                //    _log.Warn("Rad={0}, Angle={1}, angleTmp={2}, RotationZ={3}, Rot={4}", rad, Angle, _angleTmp, transfer.Position.RotationZ, transfer.Rot);
                //    _log.Warn("Distance={0}, MoveStepIndex={1}, TransferPath.Count-1={2}", Math.Abs(Distance), MoveStepIndex, TransferPath.Count - 1);
                //}

                //DespawnFlag(spwnFlag);
                if (Distance > _rangeToCheckPoint)
                {
                    //update class variables
                    //transfer.Velocity = vDistance;
                    // update Transfer variable
                    transfer.PathPointIndex = MoveStepIndex; // текущая точка, куда движемся
                    transfer.Steering = Steering; // текущий участок пути
                    // moving to the point #
                    var moveTypeTr = (TransferData)UnitMovement.GetType(UnitMovementType.Transfer);
                    moveTypeTr.UseTransferBase(transfer);

                    transfer.SetPosition(moveTypeTr.X, moveTypeTr.Y, moveTypeTr.Z, 0, 0, Helpers.ConvertRadianToSbyteDirection(moveTypeTr.Rot.Z));
                    transfer.BroadcastPacket(new SCOneUnitMovementPacket(transfer.ObjId, moveTypeTr), true);
                    RepeatMove(sim, transfer, targetX, targetY, targetZ);
                }
                else
                {
                    InPatrol = false;
                    Time = 0f;

                    // get next path or point # in current path
                    OnMove2(transfer);
                }
            }
        }

        //***************************************************************
        public Doodad SpawnFlag(float posX, float posY)
        {
            // spawn flag
            var combatFlag = new DoodadSpawner();
            combatFlag.Id = 0;
            combatFlag.UnitId = 5014; // Combat Flag Id=5014;
            combatFlag.Position = new Point();
            combatFlag.Position.ZoneId = WorldManager.Instance.GetZoneId(1, posX, posY);
            combatFlag.Position.WorldId = 1;
            combatFlag.Position.X = posX;
            combatFlag.Position.Y = posY;
            combatFlag.Position.Z = WorldManager.Instance.GetHeight(combatFlag.Position.ZoneId, combatFlag.Position.X, combatFlag.Position.Y);
            return combatFlag.Spawn(0); // set CombatFlag
        }

        //***************************************************************
        public Doodad SpawnFlag(Vector3 pos)
        {
            // spawn flag
            var combatFlag = new DoodadSpawner();
            combatFlag.Id = 0;
            combatFlag.UnitId = 5014; // Combat Flag Id=5014;
            combatFlag.Position = new Point();
            combatFlag.Position.ZoneId = WorldManager.Instance.GetZoneId(1, pos.X, pos.Y);
            combatFlag.Position.WorldId = 1;
            combatFlag.Position.X = pos.X;
            combatFlag.Position.Y = pos.Y;
            combatFlag.Position.Z = WorldManager.Instance.GetHeight(combatFlag.Position.ZoneId, combatFlag.Position.X, combatFlag.Position.Y);
            return combatFlag.Spawn(0); // set CombatFlag
        }

        //***************************************************************
        public void DespawnFlag(Doodad doodad)
        {
            // spawn flag
            var combatFlag = new DoodadSpawner();
            combatFlag.Despawn(doodad);
        }
        //***************************************************************
        public void RepeatMove(Simulation sim, Unit unit, float targetX, float targetY, float targetZ, double time = 50)
        {
            if (unit is Npc npc)
            {
                //if ((sim ?? this).AbandonTo)
                {
                    TaskManager.Instance.Schedule(new Move(sim ?? this, npc, targetX, targetY, targetZ), TimeSpan.FromMilliseconds(time));
                }
            }
            else if (unit is Transfer transfer)
            {
                //if ((sim ?? this).AbandonTo)
                {
                    TaskManager.Instance.Schedule(new Move(sim ?? this, transfer, targetX, targetY, targetZ), TimeSpan.FromMilliseconds(time));
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
                //s_log.Warn("stop moving ...");
                moveType = (ActorData)UnitMovement.GetType(UnitMovementType.Actor);
                //moveType.X = npc.Position.X;
                //moveType.Y = npc.Position.Y;
                //moveType.Z = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;
                ////-----------------------взгляд_NPC_будет(движение_откуда->движение_куда)
                //var angle = MathUtil.CalculateAngleFrom(npc.Position.X, npc.Position.Y, vPosition.X, vPosition.Y);
                //var rotZ = MathUtil.ConvertDegreeToDirection(angle);
                //moveType.RotationX = 0;
                //moveType.RotationY = 0;
                //moveType.RotationZ = rotZ;
                //moveType.Flags = 5;      // 5-walk, 4-run, 3-stand still
                //moveType.DeltaMovement = new sbyte[3];
                //moveType.DeltaMovement[0] = 0;
                //moveType.DeltaMovement[1] = 0;
                //moveType.DeltaMovement[2] = 0;

                var tmpZ = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;
                moveType.WorldPos = new WorldPos(npc.Pos.X, npc.Pos.Y, tmpZ);

                var direction = new Vector3();
                if (_vDistance != Vector3.Zero)
                {
                    direction = Vector3.Normalize(_vDistance);
                }

                var rotation = (float)Math.Atan2(direction.Y, direction.X);
                moveType.Rot = Quaternion.CreateFromAxisAngle(direction, rotation);

                moveType.Flags = 5;

                moveType.DeltaMovement = new Vector3();

                moveType.Stance = 1;     // COMBAT = 0x0, IDLE = 0x1
                moveType.Alertness = 0;  // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
                moveType.Time += 50;    // has to change all the time for normal motion.
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                MoveToPathEnabled = false;
            }
            else if (unit is Transfer transfer)
            {
                //s_log.Warn("stop moving ...");
                transfer.Speed = 0;
                transfer.RotSpeed = 0;
                transfer.Velocity = Vector3.Zero;
                _vVelocity = Vector3.Zero;

                var pp = Steering >= Routes.Count - 1 ? Routes[0][0] : Routes[Steering + 1][0];
                var vNewTarget = new Vector3(pp.X, pp.Y, pp.Z);
                rad = MathUtil.CalculateDirection(vPosition, vNewTarget);
                transfer.Rot = new Quaternion(0f, 0f, MathUtil.ConvertToDirection(rad), 1f);

                var moveTypeTr = (TransferData)UnitMovement.GetType(UnitMovementType.Transfer);
                moveTypeTr.UseTransferBase(transfer);
                transfer.BroadcastPacket(new SCOneUnitMovementPacket(transfer.ObjId, moveTypeTr), true);
                MoveToPathEnabled = false;
            }
        }

        public void PauseMove(Unit unit)
        {
            if (unit is Npc npc)
            {
                //s_log.Warn("let's stand a little...");
                moveType = (ActorData)UnitMovement.GetType(UnitMovementType.Actor);
                //moveType.X = npc.Position.X;
                //moveType.Y = npc.Position.Y;
                //moveType.Z = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;
                ////-----------------------взгляд_NPC_будет(движение_откуда->движение_куда)
                //var angle = MathUtil.CalculateAngleFrom(npc.Position.X, npc.Position.Y, vPosition.X, vPosition.Y);
                //var rotZ = MathUtil.ConvertDegreeToDirection(angle);
                //moveType.RotationX = 0;
                //moveType.RotationY = 0;
                //moveType.RotationZ = rotZ;
                //moveType.Flags = 5;      // 5-walk, 4-run, 3-stand still
                //moveType.DeltaMovement = new sbyte[3];
                //moveType.DeltaMovement[0] = 0;
                //moveType.DeltaMovement[1] = 0;
                //moveType.DeltaMovement[2] = 0;

                var tmpZ = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;
                moveType.WorldPos = new WorldPos(npc.Pos.X, npc.Pos.Y, tmpZ);

                var direction = new Vector3();
                if (_vDistance != Vector3.Zero)
                {
                    direction = Vector3.Normalize(_vDistance);
                }

                var rotation = (float)Math.Atan2(direction.Y, direction.X);
                moveType.Rot = Quaternion.CreateFromAxisAngle(direction, rotation);

                moveType.Flags = 5;

                moveType.DeltaMovement = new Vector3();

                moveType.Stance = 1;     // COMBAT = 0x0, IDLE = 0x1
                moveType.Alertness = 0;  // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
                moveType.Time += 50;    // has to change all the time for normal motion.
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
            }
            else if (unit is Transfer transfer)
            {
                //s_log.Warn("let's stand a little...");
                //s_log.Warn("pause in #" + MoveStepIndex);
                //s_log.Warn("x:" + _vPosition.X + " y:" + _vPosition.Y + " z:" + _vPosition.Z);
                transfer.Speed = 0;
                transfer.RotSpeed = 0;
                transfer.RotSpeed = 0;
                transfer.Velocity = Vector3.Zero;
                _vVelocity = Vector3.Zero;

                var pp = Steering >= Routes.Count - 1 ? Routes[0][0] : Routes[Steering + 1][0];
                var vNewTarget = new Vector3(pp.X, pp.Y, pp.Z);
                rad = MathUtil.CalculateDirection(vPosition, vNewTarget);
                transfer.Rot = new Quaternion(0f, 0f, MathUtil.ConvertToDirection(rad), 1f);

                var moveTypeTr = (TransferData)UnitMovement.GetType(UnitMovementType.Transfer);
                moveTypeTr.UseTransferBase(transfer);
                transfer.BroadcastPacket(new SCOneUnitMovementPacket(transfer.ObjId, moveTypeTr), true);
            }
        }
        public void OnMove(BaseUnit unit)
        {
            if (unit is Npc npc)
            {
                if (!MoveToPathEnabled)
                {
                    //s_log.Warn("OnMove disabled");
                    StopMove(npc);
                    return;
                }
                if (MovePath.Count > 0)
                {
                    var s = MovePath[MoveStepIndex];
                    _vPosition.X = ExtractValue(s, 1);
                    _vPosition.Y = ExtractValue(s, 2);
                    _vPosition.Z = ExtractValue(s, 3);
                    if (!PosInRange(npc, _vPosition.X, _vPosition.Y, 3))
                    {
                        RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z);
                        return;
                    }

                    if (MoveToForward)
                    {
                        if (MoveStepIndex == MovePath.Count - 1)
                        {
                            //s_log.Warn("we are ideally at the end point.");
                            //StopMove(npc);
                            PauseMove(npc);
                            MoveToForward = false; //turn back
                            MoveStepIndex--;
                            //s_log.Warn("walk to #" + MoveStepIndex);
                            s = MovePath[MoveStepIndex];
                            _vPosition.X = ExtractValue(s, 1);
                            _vPosition.Y = ExtractValue(s, 2);
                            _vPosition.Z = ExtractValue(s, 3);
                            RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z, 20000);
                            return;
                        }

                        MoveStepIndex++;
                        //s_log.Warn("we have reached checkpoint go on...");
                    }
                    else
                    {
                        if (MoveStepIndex > 0)
                        {
                            MoveStepIndex--;
                            //s_log.Warn("we reached checkpoint go further ...");
                        }
                        else
                        {
                            //s_log.Warn("we are ideally at the starting point.");
                            //StopMove(npc);
                            PauseMove(npc);
                            MoveToForward = true; //turn back
                            MoveStepIndex++;
                            //s_log.Warn("walk to #" + MoveStepIndex);
                            s = MovePath[MoveStepIndex];
                            _vPosition.X = ExtractValue(s, 1);
                            _vPosition.Y = ExtractValue(s, 2);
                            _vPosition.Z = ExtractValue(s, 3);
                            RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z, 20000);
                            return;
                        }
                    }

                    //s_log.Warn("walk to #" + MoveStepIndex);
                    s = MovePath[MoveStepIndex];
                    _vPosition.X = ExtractValue(s, 1);
                    _vPosition.Y = ExtractValue(s, 2);
                    _vPosition.Z = ExtractValue(s, 3);
                    RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z);
                }
                if (TransferPath.Count > 0)
                {
                    var s = TransferPath[MoveStepIndex];
                    _vPosition.X = s.X;
                    _vPosition.Y = s.Y;
                    _vPosition.Z = s.Z;
                    if (!PosInRange(npc, _vPosition.X, _vPosition.Y, 3))
                    {
                        RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z);
                        return;
                    }

                    if (MoveToForward)
                    {
                        if (MoveStepIndex == TransferPath.Count - 1)
                        {
                            //s_log.Warn("we are ideally at the end point.");
                            //StopMove(npc);
                            PauseMove(npc);
                            MoveToForward = false; //turn back
                            MoveStepIndex--;
                            //s_log.Warn("walk to #" + MoveStepIndex);
                            s = TransferPath[MoveStepIndex];
                            _vPosition.X = s.X;
                            _vPosition.Y = s.Y;
                            _vPosition.Z = s.Z;
                            RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z, 20000);
                            return;
                        }

                        MoveStepIndex++;
                        //s_log.Warn("we have reached checkpoint go on...");
                    }
                    else
                    {
                        if (MoveStepIndex > 0)
                        {
                            MoveStepIndex--;
                            //s_log.Warn("we reached checkpoint go further ...");
                        }
                        else
                        {
                            //s_log.Warn("we are ideally at the starting point.");
                            //StopMove(npc);
                            PauseMove(npc);
                            MoveToForward = true; //turn back
                            MoveStepIndex++;
                            //s_log.Warn("walk to #" + MoveStepIndex);
                            s = TransferPath[MoveStepIndex];
                            _vPosition.X = s.X;
                            _vPosition.Y = s.Y;
                            _vPosition.Z = s.Z;
                            RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z, 20000);
                            return;
                        }
                    }

                    //s_log.Warn("walk to #" + MoveStepIndex);
                    s = TransferPath[MoveStepIndex];
                    _vPosition.X = s.X;
                    _vPosition.Y = s.Y;
                    _vPosition.Z = s.Z;
                    RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z);
                }
            }
            else if (unit is Transfer transfer)
            {
                if (!MoveToPathEnabled)
                {
                    //s_log.Warn("OnMove disabled");
                    StopMove(transfer);
                    return;
                }
                if (MovePath.Count > 0)
                {
                    var s = MovePath[MoveStepIndex];
                    _vPosition.X = ExtractValue(s, 1);
                    _vPosition.Y = ExtractValue(s, 2);
                    _vPosition.Z = ExtractValue(s, 3);
                    if (!PosInRange(transfer, _vPosition.X, _vPosition.Y, 3))
                    {
                        RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z);
                        return;
                    }

                    if (MoveToForward)
                    {
                        if (MoveStepIndex == MovePath.Count - 1)
                        {
                            //s_log.Warn("we are at the end point.");
                            //StopMove(npc);
                            PauseMove(transfer);
                            MoveToForward = false; //turn back
                            MoveStepIndex--;
                            //s_log.Warn("walk to #" + MoveStepIndex);
                            s = MovePath[MoveStepIndex];
                            _vPosition.X = ExtractValue(s, 1);
                            _vPosition.Y = ExtractValue(s, 2);
                            _vPosition.Z = ExtractValue(s, 3);
                            RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z, transfer.Template.TransferPaths[Steering].WaitTimeEnd * 1000);
                            return;
                        }

                        MoveStepIndex++;
                        //s_log.Warn("we reached checkpoint go further...");
                    }
                    else
                    {
                        if (MoveStepIndex > 0)
                        {
                            MoveStepIndex--;
                            //s_log.Warn("we reached checkpoint go further ...");
                        }
                        else
                        {
                            //s_log.Warn("we are at the starting point.");
                            //StopMove(npc);
                            PauseMove(transfer);
                            MoveToForward = true; //turn back
                            MoveStepIndex++;
                            //s_log.Warn("walk to #" + MoveStepIndex);
                            s = MovePath[MoveStepIndex];
                            _vPosition.X = ExtractValue(s, 1);
                            _vPosition.Y = ExtractValue(s, 2);
                            _vPosition.Z = ExtractValue(s, 3);
                            RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z, transfer.Template.TransferPaths[Steering].WaitTimeStart * 1000);
                            return;
                        }
                    }

                    //s_log.Warn("walk to #" + MoveStepIndex);
                    s = MovePath[MoveStepIndex];
                    _vPosition.X = ExtractValue(s, 1);
                    _vPosition.Y = ExtractValue(s, 2);
                    _vPosition.Z = ExtractValue(s, 3);
                    RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z);
                }
                if (TransferPath.Count > 0)
                {
                    var s = TransferPath[MoveStepIndex];
                    _vPosition.X = s.X;
                    _vPosition.Y = s.Y;
                    _vPosition.Z = s.Z;
                    if (PosInRange(transfer, _vPosition.X, _vPosition.Y, 50))
                    {
                        if (MoveToForward)
                        {
                            if (MoveStepIndex == TransferPath.Count - 1)
                            {
                                //s_log.Warn("we are at the end point.");
                                //StopMove(npc);
                                PauseMove(transfer);
                                MoveToForward = false; //turn back
                                MoveStepIndex--;
                                //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                                //s_log.Warn("walk to #" + MoveStepIndex);
                                s = TransferPath[MoveStepIndex];
                                _vPosition.X = s.X;
                                _vPosition.Y = s.Y;
                                _vPosition.Z = s.Z;
                                RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z, transfer.Template.TransferPaths[Steering].WaitTimeEnd * 1000);
                                return;
                            }
                            MoveStepIndex++;
                            //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                            //s_log.Warn("we reached checkpoint go further...");
                        }
                        else
                        {
                            if (MoveStepIndex > 0)
                            {
                                MoveStepIndex--;
                                //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                                //s_log.Warn("we reached checkpoint go further...");
                            }
                            else
                            {
                                //s_log.Warn("we are at the starting point.");
                                //StopMove(npc);
                                PauseMove(transfer);
                                MoveToForward = true; //turn back
                                MoveStepIndex++;
                                //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                                //s_log.Warn("walk to #" + MoveStepIndex);
                                s = TransferPath[MoveStepIndex];
                                _vPosition.X = s.X;
                                _vPosition.Y = s.Y;
                                _vPosition.Z = s.Z;
                                RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z, transfer.Template.TransferPaths[Steering].WaitTimeStart * 1000);
                                return;
                            }
                        }
                        //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                        //s_log.Warn("walk to #" + MoveStepIndex);
                        s = TransferPath[MoveStepIndex];
                        _vPosition.X = s.X;
                        _vPosition.Y = s.Y;
                        _vPosition.Z = s.Z;
                        RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z);
                        return;
                    }
                    //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                    //s_log.Warn("to any point is very far, we stop.");
                }
            }
        }

        private void OnMove2(BaseUnit unit)
        {
            if (unit is Npc npc)
            {
                if (!MoveToPathEnabled)
                {
                    //s_log.Warn("OnMove disabled");
                    StopMove(npc);
                    return;
                }
                if (MovePath.Count > 0)
                {
                    var s = MovePath[MoveStepIndex];
                    _vPosition.X = ExtractValue(s, 1);
                    _vPosition.Y = ExtractValue(s, 2);
                    _vPosition.Z = ExtractValue(s, 3);
                    if (!PosInRange(npc, _vPosition.X, _vPosition.Y, 3))
                    {
                        RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z);
                        return;
                    }

                    if (MoveToForward)
                    {
                        if (MoveStepIndex == MovePath.Count - 1)
                        {
                            //s_log.Warn("we are ideally at the end point.");
                            //StopMove(npc);
                            PauseMove(npc);
                            MoveToForward = false; //turn back
                            MoveStepIndex--;
                            //s_log.Warn("walk to #" + MoveStepIndex);
                            s = MovePath[MoveStepIndex];
                            _vPosition.X = ExtractValue(s, 1);
                            _vPosition.Y = ExtractValue(s, 2);
                            _vPosition.Z = ExtractValue(s, 3);
                            RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z, 20000);
                            return;
                        }

                        MoveStepIndex++;
                        //s_log.Warn("we have reached checkpoint go on...");
                    }
                    else
                    {
                        if (MoveStepIndex > 0)
                        {
                            MoveStepIndex--;
                            //s_log.Warn("we reached checkpoint go further ...");
                        }
                        else
                        {
                            //s_log.Warn("we are ideally at the starting point.");
                            //StopMove(npc);
                            PauseMove(npc);
                            MoveToForward = true; //turn back
                            MoveStepIndex++;
                            //s_log.Warn("walk to #" + MoveStepIndex);
                            s = MovePath[MoveStepIndex];
                            _vPosition.X = ExtractValue(s, 1);
                            _vPosition.Y = ExtractValue(s, 2);
                            _vPosition.Z = ExtractValue(s, 3);
                            RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z, 20000);
                            return;
                        }
                    }

                    //s_log.Warn("walk to #" + MoveStepIndex);
                    s = MovePath[MoveStepIndex];
                    _vPosition.X = ExtractValue(s, 1);
                    _vPosition.Y = ExtractValue(s, 2);
                    _vPosition.Z = ExtractValue(s, 3);
                    RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z);
                }
                if (TransferPath.Count > 0)
                {
                    var s = TransferPath[MoveStepIndex];
                    _vPosition.X = s.X;
                    _vPosition.Y = s.Y;
                    _vPosition.Z = s.Z;
                    if (!PosInRange(npc, _vPosition.X, _vPosition.Y, 3))
                    {
                        RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z);
                        return;
                    }

                    if (MoveToForward)
                    {
                        if (MoveStepIndex == TransferPath.Count - 1)
                        {
                            //s_log.Warn("we are ideally at the end point.");
                            //StopMove(npc);
                            PauseMove(npc);
                            MoveToForward = false; //turn back
                            MoveStepIndex--;
                            //s_log.Warn("walk to #" + MoveStepIndex);
                            s = TransferPath[MoveStepIndex];
                            _vPosition.X = s.X;
                            _vPosition.Y = s.Y;
                            _vPosition.Z = s.Z;
                            RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z, 20000);
                            return;
                        }

                        MoveStepIndex++;
                        //s_log.Warn("we have reached checkpoint go on...");
                    }
                    else
                    {
                        if (MoveStepIndex > 0)
                        {
                            MoveStepIndex--;
                            //s_log.Warn("we reached checkpoint go further ...");
                        }
                        else
                        {
                            //s_log.Warn("we are ideally at the starting point.");
                            //StopMove(npc);
                            PauseMove(npc);
                            MoveToForward = true; //turn back
                            MoveStepIndex++;
                            //s_log.Warn("walk to #" + MoveStepIndex);
                            s = TransferPath[MoveStepIndex];
                            _vPosition.X = s.X;
                            _vPosition.Y = s.Y;
                            _vPosition.Z = s.Z;
                            RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z, 20000);
                            return;
                        }
                    }

                    //s_log.Warn("walk to #" + MoveStepIndex);
                    s = TransferPath[MoveStepIndex];
                    _vPosition.X = s.X;
                    _vPosition.Y = s.Y;
                    _vPosition.Z = s.Z;
                    RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z);
                }
            }
            else if (unit is Transfer transfer)
            {
                if (!MoveToPathEnabled)
                {
                    //s_log.Warn("OnMove disabled");
                    StopMove(transfer);
                    return;
                }
                if (MovePath.Count > 0)
                {
                    var s = MovePath[MoveStepIndex];
                    _vPosition.X = ExtractValue(s, 1);
                    _vPosition.Y = ExtractValue(s, 2);
                    _vPosition.Z = ExtractValue(s, 3);
                    if (!PosInRange(transfer, _vPosition.X, _vPosition.Y, 3))
                    {
                        RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z);
                        return;
                    }

                    if (MoveToForward)
                    {
                        if (MoveStepIndex == MovePath.Count - 1)
                        {
                            //s_log.Warn("we are at the end point.");
                            PauseMove(transfer);
                            MoveToForward = false; //turn back
                            MoveStepIndex--;
                            //s_log.Warn("walk to #" + MoveStepIndex);
                            s = MovePath[MoveStepIndex];
                            _vPosition.X = ExtractValue(s, 1);
                            _vPosition.Y = ExtractValue(s, 2);
                            _vPosition.Z = ExtractValue(s, 3);
                            double time = 0;
                            if (transfer.Template.TransferPaths[Steering].WaitTimeEnd > 0)
                            {
                                time = transfer.Template.TransferPaths[Steering].WaitTimeEnd;
                            }
                            if (transfer.Template.TransferPaths[Steering].WaitTimeStart > 0)
                            {
                                time = transfer.Template.TransferPaths[Steering].WaitTimeStart;
                            }
                            RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z, time * 1000);
                            return;
                        }

                        MoveStepIndex++;
                        //s_log.Warn("we reached checkpoint go further...");
                    }
                    else
                    {
                        if (MoveStepIndex > 0)
                        {
                            MoveStepIndex--;
                            //s_log.Warn("we reached checkpoint go further ...");
                        }
                        else
                        {
                            //s_log.Warn("we are at the starting point.");
                            PauseMove(transfer);
                            MoveToForward = true; //turn back
                            MoveStepIndex++;
                            //s_log.Warn("walk to #" + MoveStepIndex);
                            s = MovePath[MoveStepIndex];
                            _vPosition.X = ExtractValue(s, 1);
                            _vPosition.Y = ExtractValue(s, 2);
                            _vPosition.Z = ExtractValue(s, 3);
                            double time = 0;
                            if (transfer.Template.TransferPaths[Steering].WaitTimeEnd > 0)
                            {
                                time = transfer.Template.TransferPaths[Steering].WaitTimeEnd;
                            }
                            if (transfer.Template.TransferPaths[Steering].WaitTimeStart > 0)
                            {
                                time = transfer.Template.TransferPaths[Steering].WaitTimeStart;
                            }
                            RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z, time * 1000);
                            return;
                        }
                    }

                    //s_log.Warn("walk to #" + MoveStepIndex);
                    s = MovePath[MoveStepIndex];
                    _vPosition.X = ExtractValue(s, 1);
                    _vPosition.Y = ExtractValue(s, 2);
                    _vPosition.Z = ExtractValue(s, 3);
                    RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z);
                }
                if (TransferPath.Count > 0)
                {
                    var s = TransferPath[MoveStepIndex];
                    _vPosition.X = s.X;
                    _vPosition.Y = s.Y;
                    _vPosition.Z = s.Z;
                    // var carriage = TransferManager.Instance.GetTransferTemplate(transfer.TemplateId);
                    if (PosInRange(transfer, _vPosition.X, _vPosition.Y, 50))
                    {
                        if (MoveToForward)
                        {
                            /*
                              Проходим по очереди все участки пути из списка, с начала каждого пути.
                              Начиная с середины, это участки в обратную сторону.
                            */
                            if (MoveStepIndex >= TransferPath.Count - 1)
                            {
                                // участок пути закончился
                                MoveStepIndex = 0;
                                if (Steering >= Routes.Count - 1)
                                {
                                    // закончились все участки пути дороги, нужно начать сначала
                                    Steering = 0; // укажем на начальный путь
                                                  //s_log.Warn("we are at the end point.");
                                    if (!Loop)
                                    {
                                        PauseMove(transfer); // останавливаемся
                                    }
                                    else
                                    {
                                        LoadTransferPathFromRoutes(Steering); // загрузим путь в TransferPath
                                                                              //s_log.Info("Transfer #" + transfer.TemplateId);
                                                                              //s_log.Warn("next path #" + Steering);
                                                                              //s_log.Warn("walk to #" + MoveStepIndex);
                                                                              //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                                        s = TransferPath[MoveStepIndex];
                                        _vPosition.X = s.X;
                                        _vPosition.Y = s.Y;
                                        _vPosition.Z = s.Z;
                                        // здесь будет непосредственно пауза между участками дороги, если она есть в базе данных
                                        var time = transfer.Template.TransferPaths[Steering].WaitTimeStart;
                                        RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z, time * 1000);
                                    }
                                }
                                else
                                {
                                    // продолжим путь
                                    var time = transfer.Template.TransferPaths[Steering].WaitTimeEnd;
                                    Steering++; // укажем на следующий участок пути
                                    LoadTransferPathFromRoutes(Steering); // загрузим путь в TransferPath
                                                                          //LoadTransferPath2(transfer.TemplateId, Steering); // загрузим путь в TransferPath
                                                                          //s_log.Info("Transfer #" + transfer.TemplateId);
                                                                          //s_log.Warn("path #" + Steering);
                                                                          //s_log.Warn("walk to #" + MoveStepIndex);
                                                                          //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                                    s = TransferPath[MoveStepIndex];
                                    _vPosition.X = s.X;
                                    _vPosition.Y = s.Y;
                                    _vPosition.Z = s.Z;
                                    // здесь будет непосредственно пауза между участками дороги
                                    RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z, time * 1000);
                                }
                            }
                            else
                            {
                                MoveStepIndex++;
                                //s_log.Warn("we reached checkpoint go further...");
                                //s_log.Info("Transfer #" + transfer.TemplateId);
                                //s_log.Warn("path #" + Steering);
                                //s_log.Warn("walk to #" + MoveStepIndex);
                                //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                                s = TransferPath[MoveStepIndex];
                                _vPosition.X = s.X;
                                _vPosition.Y = s.Y;
                                _vPosition.Z = s.Z;
                                //if (MoveStepIndex - 1 == 0)
                                //{
                                //    // здесь будет пауза в начале участка пути, если она есть в базе данных
                                //    var time = transfer.Template.TransferPaths[Steering].WaitTimeStart;
                                //    RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z, time * 1000);
                                //}
                                //else
                                //{
                                // иначе, паузу не делаем
                                RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z);
                                //}
                            }
                        }
                        else
                        {
                            /*
                              здесь всё тоже самое, но с конца пути
                              здесь проходим все пути из списка дорог по очереди, с конца каждого пути, начиная с середины это пути в обратную сторону
                            */
                            if (MoveStepIndex == 0)
                            {
                                //s_log.Warn("we are at the begin point.");
                                // путь закончился
                                Steering--; // укажем на предыдущий путь
                                if (Steering < 0)
                                {
                                    // закончились дороги, нужно начать с конца
                                    Steering = Routes.Count - 1; // укажем на самый последний в списке путь
                                    PauseMove(transfer); // продолжим путь после паузы назад
                                                         //LoadTransferPath(Steering); // загрузим путь в TransferPath
                                                         //LoadTransferPath2(transfer.TemplateId, Steering); // загрузим путь в TransferPath
                                    LoadTransferPathFromRoutes(Steering); // загрузим путь в TransferPath

                                    MoveStepIndex = TransferPath.Count - 1; // укажем на последнюю точку в пути
                                                                            //s_log.Info("Transfer #" + transfer.TemplateId);
                                                                            //s_log.Warn("next path #" + Steering);
                                                                            //s_log.Warn("walk to #" + MoveStepIndex);
                                                                            //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                                    s = TransferPath[MoveStepIndex];
                                    _vPosition.X = s.X;
                                    _vPosition.Y = s.Y;
                                    _vPosition.Z = s.Z;
                                    // здесь непосредственно пауза
                                    double time = 0;
                                    if (transfer.Template.TransferPaths[Steering].WaitTimeEnd > 0)
                                    {
                                        time = transfer.Template.TransferPaths[Steering].WaitTimeEnd;
                                    }
                                    if (transfer.Template.TransferPaths[Steering].WaitTimeStart > 0)
                                    {
                                        time = transfer.Template.TransferPaths[Steering].WaitTimeStart;
                                    }
                                    RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z, time * 1000);
                                }
                                else
                                {
                                    // продолжим путь
                                    //LoadTransferPath(Steering); // загрузим путь в TransferPath
                                    //LoadTransferPath2(transfer.TemplateId, Steering); // загрузим путь в TransferPath
                                    LoadTransferPathFromRoutes(Steering); // загрузим путь в TransferPath
                                    MoveStepIndex = TransferPath.Count - 1; // укажем на последнюю точку в пути
                                                                            //s_log.Info("Transfer #" + transfer.TemplateId);
                                                                            //s_log.Warn("path #" + Steering);
                                                                            //s_log.Warn("walk to #" + MoveStepIndex);
                                                                            //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                                    s = TransferPath[MoveStepIndex];
                                    _vPosition.X = s.X;
                                    _vPosition.Y = s.Y;
                                    _vPosition.Z = s.Z;
                                    // здесь будет непосредственно пауза между участками дороги, если она есть в базе данных
                                    double time = 0;
                                    if (transfer.Template.TransferPaths[Steering].WaitTimeEnd > 0)
                                    {
                                        time = transfer.Template.TransferPaths[Steering].WaitTimeEnd;
                                    }
                                    if (transfer.Template.TransferPaths[Steering].WaitTimeStart > 0)
                                    {
                                        time = transfer.Template.TransferPaths[Steering].WaitTimeStart;
                                    }
                                    RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z, time * 1000);
                                }
                            }
                            else
                            {
                                MoveStepIndex--;
                                //s_log.Warn("we reached checkpoint go further...");
                                //s_log.Info("Transfer #" + transfer.TemplateId);
                                //s_log.Warn("path #" + Steering);
                                //s_log.Warn("walk to #" + MoveStepIndex);
                                //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                                s = TransferPath[MoveStepIndex];
                                _vPosition.X = s.X;
                                _vPosition.Y = s.Y;
                                _vPosition.Z = s.Z;
                                // здесь нет паузы
                                RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z);
                            }
                        }
                    }
                    else
                    {
                        //s_log.Info("Transfer #" + transfer.TemplateId);
                        //s_log.Warn("path #" + Steering);
                        //s_log.Warn("checkpoint #" + MoveStepIndex);
                        //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                        //s_log.Warn("to any point is very far, we stop.");
                    }
                }
            }
        }
        public void NextPathOrPointInPath(Unit unit)
        {
            if (unit is Npc npc)
            {
                if (!MoveToPathEnabled)
                {
                    //s_log.Warn("Move disabled");
                    StopMove(npc);
                    return;
                }
                if (MovePath.Count > 0)
                {
                    var s = MovePath[MoveStepIndex];
                    _vPosition.X = ExtractValue(s, 1);
                    _vPosition.Y = ExtractValue(s, 2);
                    _vPosition.Z = ExtractValue(s, 3);
                    if (!PosInRange(npc, _vPosition.X, _vPosition.Y, 3))
                    {
                        RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z);
                        return;
                    }

                    if (MoveToForward)
                    {
                        if (MoveStepIndex == MovePath.Count - 1)
                        {
                            //s_log.Warn("we are at the end point.");
                            //StopMove(npc);
                            PauseMove(npc);
                            MoveToForward = false; //turn back
                            MoveStepIndex--;
                            //s_log.Warn("walk to #" + MoveStepIndex);
                            s = MovePath[MoveStepIndex];
                            _vPosition.X = ExtractValue(s, 1);
                            _vPosition.Y = ExtractValue(s, 2);
                            _vPosition.Z = ExtractValue(s, 3);
                            RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z, 20000);
                            return;
                        }

                        MoveStepIndex++;
                        //s_log.Warn("we have reached checkpoint go on...");
                    }
                    else
                    {
                        if (MoveStepIndex > 0)
                        {
                            MoveStepIndex--;
                            //s_log.Warn("we reached checkpoint go further ...");
                        }
                        else
                        {
                            //s_log.Warn("we are at the starting point.");
                            //StopMove(npc);
                            PauseMove(npc);
                            MoveToForward = true; //turn back
                            MoveStepIndex++;
                            //s_log.Warn("walk to #" + MoveStepIndex);
                            s = MovePath[MoveStepIndex];
                            _vPosition.X = ExtractValue(s, 1);
                            _vPosition.Y = ExtractValue(s, 2);
                            _vPosition.Z = ExtractValue(s, 3);
                            RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z, 20000);
                            return;
                        }
                    }

                    //s_log.Warn("walk to #" + MoveStepIndex);
                    s = MovePath[MoveStepIndex];
                    _vPosition.X = ExtractValue(s, 1);
                    _vPosition.Y = ExtractValue(s, 2);
                    _vPosition.Z = ExtractValue(s, 3);
                    RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z);
                }
                if (TransferPath.Count > 0)
                {
                    var s = TransferPath[MoveStepIndex];
                    _vPosition.X = s.X;
                    _vPosition.Y = s.Y;
                    _vPosition.Z = s.Z;
                    if (!PosInRange(npc, _vPosition.X, _vPosition.Y, 3))
                    {
                        RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z);
                        return;
                    }

                    if (MoveToForward)
                    {
                        if (MoveStepIndex == TransferPath.Count - 1)
                        {
                            //s_log.Warn("we are at the end point.");
                            //StopMove(npc);
                            PauseMove(npc);
                            MoveToForward = false; //turn back
                            MoveStepIndex--;
                            //s_log.Warn("walk to #" + MoveStepIndex);
                            s = TransferPath[MoveStepIndex];
                            _vPosition.X = s.X;
                            _vPosition.Y = s.Y;
                            _vPosition.Z = s.Z;
                            RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z, 20000);
                            return;
                        }

                        MoveStepIndex++;
                        //s_log.Warn("we have reached checkpoint go on...");
                    }
                    else
                    {
                        if (MoveStepIndex > 0)
                        {
                            MoveStepIndex--;
                            //s_log.Warn("we reached checkpoint go further ...");
                        }
                        else
                        {
                            //s_log.Warn("we are at the starting point.");
                            //StopMove(npc);
                            PauseMove(npc);
                            MoveToForward = true; //turn back
                            MoveStepIndex++;
                            //s_log.Warn("walk to #" + MoveStepIndex);
                            s = TransferPath[MoveStepIndex];
                            _vPosition.X = s.X;
                            _vPosition.Y = s.Y;
                            _vPosition.Z = s.Z;
                            RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z, 20000);
                            return;
                        }
                    }

                    //s_log.Warn("walk to #" + MoveStepIndex);
                    s = TransferPath[MoveStepIndex];
                    _vPosition.X = s.X;
                    _vPosition.Y = s.Y;
                    _vPosition.Z = s.Z;
                    RepeatMove(this, npc, _vPosition.X, _vPosition.Y, _vPosition.Z);
                }
            }
            else if (unit is Transfer transfer)
            {
                if (!MoveToPathEnabled || transfer.Position == null || !transfer.IsInPatrol)
                {
                    //s_log.Warn("Move disabled");
                    StopMove(transfer);
                    return;
                }
                if (MovePath.Count > 0)
                {
                    var s = MovePath[MoveStepIndex];
                    _vPosition.X = ExtractValue(s, 1);
                    _vPosition.Y = ExtractValue(s, 2);
                    _vPosition.Z = ExtractValue(s, 3);
                    if (!PosInRange(transfer, _vPosition.X, _vPosition.Y, 3))
                    {
                        RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z);
                        return;
                    }

                    if (MoveToForward)
                    {
                        if (MoveStepIndex == MovePath.Count - 1)
                        {
                            //s_log.Warn("we are at the end point.");
                            //StopMove(npc);
                            PauseMove(transfer);
                            MoveToForward = false; //turn back
                            MoveStepIndex--;
                            //s_log.Warn("walk to #" + MoveStepIndex);
                            s = MovePath[MoveStepIndex];
                            _vPosition.X = ExtractValue(s, 1);
                            _vPosition.Y = ExtractValue(s, 2);
                            _vPosition.Z = ExtractValue(s, 3);
                            RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z, transfer.Template.WaitTime * 1000);
                            return;
                        }

                        MoveStepIndex++;
                        //s_log.Warn("we reached checkpoint go further...");
                    }
                    else
                    {
                        if (MoveStepIndex > 0)
                        {
                            MoveStepIndex--;
                            //s_log.Warn("we reached checkpoint go further ...");
                        }
                        else
                        {
                            //s_log.Warn("we are at the starting point.");
                            //StopMove(npc);
                            PauseMove(transfer);
                            MoveToForward = true; //turn back
                            MoveStepIndex++;
                            //s_log.Warn("walk to #" + MoveStepIndex);
                            s = MovePath[MoveStepIndex];
                            _vPosition.X = ExtractValue(s, 1);
                            _vPosition.Y = ExtractValue(s, 2);
                            _vPosition.Z = ExtractValue(s, 3);
                            RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z, transfer.Template.WaitTime * 1000);
                            return;
                        }
                    }

                    //s_log.Warn("walk to #" + MoveStepIndex);
                    s = MovePath[MoveStepIndex];
                    _vPosition.X = ExtractValue(s, 1);
                    _vPosition.Y = ExtractValue(s, 2);
                    _vPosition.Z = ExtractValue(s, 3);
                    RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z);
                }
                if (TransferPath.Count > 0)
                {
                    var s = TransferPath[MoveStepIndex];
                    _vPosition.X = s.X;
                    _vPosition.Y = s.Y;
                    _vPosition.Z = s.Z;
                    //var carriage = TransferManager.Instance.GetTransferTemplate(transfer.TemplateId);
                    if (PosInRange(transfer, _vPosition.X, _vPosition.Y, 50))
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
                                if (Steering == _allRoutes[transfer.TemplateId].Count)
                                {
                                    // закончились дороги, нужно начать сначала
                                    Steering = 0; // укажем на начальный путь
                                                  // нужно разворачиваться
                                                  //s_log.Warn("we are at the end point.");
                                    PauseMove(transfer);
                                    // продолжим путь после паузы назад
                                    LoadTransferPath2(transfer.TemplateId, Steering); // загрузим путь в TransferPath
                                    MoveStepIndex = 0;
                                    //s_log.Info("Transfer #" + transfer.TemplateId);
                                    //s_log.Warn("next path #" + Steering);
                                    //s_log.Warn("walk to #" + MoveStepIndex);
                                    //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                                    s = TransferPath[MoveStepIndex];
                                    _vPosition.X = s.X;
                                    _vPosition.Y = s.Y;
                                    _vPosition.Z = s.Z;
                                    // здесь будет непосредственно пауза между участками дороги, если она есть в базе данных
                                    RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z, transfer.Template.TransferPaths[Steering].WaitTimeStart * 1000);
                                }
                                //else if (Steering == AllRoutes[transfer.TemplateId].Count / 2)
                                //{
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
                                    // продолжим путь
                                    //LoadTransferPath(Steering); // загрузим путь в TransferPath
                                    LoadTransferPath2(transfer.TemplateId, Steering); // загрузим путь в TransferPath
                                    MoveStepIndex = 0;
                                    //s_log.Info("Transfer #" + transfer.TemplateId);
                                    //s_log.Warn("path #" + Steering);
                                    //s_log.Warn("walk to #" + MoveStepIndex);
                                    //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                                    s = TransferPath[MoveStepIndex];
                                    _vPosition.X = s.X;
                                    _vPosition.Y = s.Y;
                                    _vPosition.Z = s.Z;
                                    // здесь нет паузы
                                    RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z);
                                }
                            }
                            else
                            {
                                MoveStepIndex++;
                                //s_log.Warn("we reached checkpoint go further...");
                                //s_log.Info("Transfer #" + transfer.TemplateId);
                                //s_log.Warn("path #" + Steering);
                                //s_log.Warn("walk to #" + MoveStepIndex);
                                //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                                s = TransferPath[MoveStepIndex];
                                _vPosition.X = s.X;
                                _vPosition.Y = s.Y;
                                _vPosition.Z = s.Z;
                                // здесь нет паузы
                                RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z);
                            }
                        }
                        else
                        {
                            //TODO здесь всё тоже самое, но с конца пути
                            //TODO здесь проходим все пути из списка дорог по очереди, с конца каждого пути, начиная с середины это пути в обратную сторону
                            if (MoveStepIndex == 0)
                            {
                                //s_log.Warn("we are at the begin point.");
                                // путь закончился
                                Steering--; // укажем на предыдущий путь
                                if (Steering < 0)
                                {
                                    // закончились дороги, нужно начать с конца
                                    Steering = _allRoutes[transfer.TemplateId].Count - 1; // укажем на самый последний в списке путь
                                    PauseMove(transfer); // продолжим путь после паузы назад
                                                         //LoadTransferPath(Steering); // загрузим путь в TransferPath
                                    LoadTransferPath2(transfer.TemplateId, Steering); // загрузим путь в TransferPath
                                    MoveStepIndex = TransferPath.Count - 1; // укажем на последнюю точку в пути
                                                                            //s_log.Info("Transfer #" + transfer.TemplateId);
                                                                            //s_log.Warn("next path #" + Steering);
                                                                            //s_log.Warn("walk to #" + MoveStepIndex);
                                                                            //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                                    s = TransferPath[MoveStepIndex];
                                    _vPosition.X = s.X;
                                    _vPosition.Y = s.Y;
                                    _vPosition.Z = s.Z;
                                    // здесь непосредственно пауза
                                    RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z, transfer.Template.WaitTime * 1000);
                                }
                                else if (Steering == _allRoutes[transfer.TemplateId].Count / 2 - 1)
                                {
                                    // достигли середины списка, нужно разворачиваться
                                    //s_log.Warn("have reached the middle of the list, we need to turn around...");
                                    PauseMove(transfer); // продолжим путь после паузы назад
                                                         //LoadTransferPath(Steering); // загрузим путь в TransferPath
                                    LoadTransferPath2(transfer.TemplateId, Steering); // загрузим путь в TransferPath
                                    MoveStepIndex = TransferPath.Count - 1; // укажем на последнюю точку в пути
                                                                            //s_log.Info("Transfer #" + transfer.TemplateId);
                                                                            //s_log.Warn("next path #" + Steering);
                                                                            //s_log.Warn("walk to #" + MoveStepIndex);
                                                                            //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                                    s = TransferPath[MoveStepIndex];
                                    _vPosition.X = s.X;
                                    _vPosition.Y = s.Y;
                                    _vPosition.Z = s.Z;
                                    // здесь непосредственно пауза
                                    RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z, transfer.Template.WaitTime * 1000);
                                }
                                else
                                {
                                    // продолжим путь
                                    //LoadTransferPath(Steering); // загрузим путь в TransferPath
                                    LoadTransferPath2(transfer.TemplateId, Steering); // загрузим путь в TransferPath
                                    MoveStepIndex = TransferPath.Count - 1; // укажем на последнюю точку в пути
                                                                            //s_log.Info("Transfer #" + transfer.TemplateId);
                                                                            //s_log.Warn("path #" + Steering);
                                                                            //s_log.Warn("walk to #" + MoveStepIndex);
                                                                            //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                                    s = TransferPath[MoveStepIndex];
                                    _vPosition.X = s.X;
                                    _vPosition.Y = s.Y;
                                    _vPosition.Z = s.Z;
                                    // здесь нет паузы
                                    RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z);
                                }
                            }
                            else
                            {
                                MoveStepIndex--;
                                //s_log.Warn("we reached checkpoint go further...");
                                //s_log.Info("Transfer #" + transfer.TemplateId);
                                //s_log.Warn("path #" + Steering);
                                //s_log.Warn("walk to #" + MoveStepIndex);
                                //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                                s = TransferPath[MoveStepIndex];
                                _vPosition.X = s.X;
                                _vPosition.Y = s.Y;
                                _vPosition.Z = s.Z;
                                // здесь нет паузы
                                RepeatMove(this, transfer, _vPosition.X, _vPosition.Y, _vPosition.Z);
                            }
                        }
                    }
                    else
                    {
                        //s_log.Info("Transfer #" + transfer.TemplateId);
                        //s_log.Warn("path #" + Steering);
                        //s_log.Warn("checkpoint #" + MoveStepIndex);
                        //s_log.Warn("x:=" + _vPosition.X + " y:=" + _vPosition.Y + " z:=" + _vPosition.Z);
                        //s_log.Warn("to any point is very far, we stop.");
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
                //s_log.Info("Loading {0} transfer_path...", GetMoveFileName());
            }
            catch (Exception e)
            {
                //s_log.Warn("Error in read MovePath: {0}", e);
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
            //s_log.Info("Transfer: Loading {0} transfer_path...", namePath);
            TransferPath = TransferManager.Instance.GetTransferPath(namePath);
            return TransferPath;
        }
        public void LoadTransferPathFromRoutes(int steering) // загрузить путь под номером steering
        {
            //s_log.Info("Transfer: Loading {0} transfer_path...", steering);
            TransferPath = Routes[steering];
        }

        public void ReadPath(string namePath) //Вызывается при включении скрипта
        {
            //s_log.Info("Transfer: Reading {0} transfer_path...", namePath);
            TransferPath = TransferManager.Instance.GetTransferPath(namePath);
        }

        public void AddPath(string namePath) //Добавить продолжение маршрута
        {
            //s_log.Info("Transfer: Adding {0} transfer_path...", namePath);
            TransferPath.AddRange(TransferManager.Instance.GetTransferPath(namePath));
        }

        public override void Execute(BaseUnit unit)
        {
            throw new NotImplementedException();
        }

        public override void Execute(Npc npc)
        {
            OnMove(npc);
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
