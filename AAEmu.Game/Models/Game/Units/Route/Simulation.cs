using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Tasks.UnitMove;
using AAEmu.Game.Utils;

using NLog;

namespace AAEmu.Game.Models.Game.Units.Route
{
    /// <summary>
    /// Control NPC to move along this route
    /// </summary>
    public class Simulation : Patrol
    {
        public Simulation(Unit unit)
        {
            Init(unit);
        }

        private static Logger _log = LogManager.GetCurrentClassLogger();

        public Character character;
        public Npc npc;

        public bool AbandonTo { get; set; } = false; // для прерывания repeat()
        public Point Position { get; set; } // Target position

        //// movement data
        public List<string> MovePath;     //  the data we're going to be moving on at the moment
        public List<string> RecordPath;   //  данные для записи пути
        public int PointsCount { get; set; }              // кол-во поинтов в процессе записи пути
        public bool SavePathEnabled { get; set; }         // флаг записи пути
        public bool MoveToPathEnabled { get; set; }       // флаг движения по пути
        public bool MoveToForward { get; set; }           // направление движения да - вперед, нет - назад
        public bool runningMode { get; set; } = false;    // режим движения да - бежать, нет - идти
        public int MoveStepIndex { get; set; }            // текущ. чекпоинт (куда бежим сейчас)
        int oldTime, chkTime;
        float oldX, oldY, oldZ;
        //*******************************************************
        public string RecordFilesPath = @"./bin/debug/netcoreapp2.2/Data/Path/";       // путь где хранятся наши файлы
        public string RecordFileExt = @".path";       // расширение по умолчанию
        public string MoveFilesPath = @"./bin/debug/netcoreapp2.2/Data/Path/";         // путь где хранятся наши файлы
        public string MoveFileExt = @".path";         // расширение по умолчанию
        public string MoveFileName = "";         // расширение по умолчанию
        private float MovingDistance = 0.25f; //0.3f;
        float RangeToCheckPoint = 0.5f; // дистанция до чекпоинта при которой считается , что мы достигли оного
        int MoveTrigerDelay = 1000;     // срабатывание таймера на движение  0,8 сек
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


        public override void Execute(Npc npc)
        {
            OnMove(npc);
        }
        //***************************************************************
        //ПЕРЕМЕЩЕНИЕ:
        //Идти в точку с координатами x,y,z
        //MOVETO(npc, x, y, z)

        //***************************************************************
        //возвращает растоянием между 2 точками
        public int Delta(float positionX1, float positionY1, float positionX2, float positionY2)
        {
            //return Math.Round(Math.Sqrt((positionX1-positionX2)*(positionX1-positionX2))+(positionY1-positionY2)*(positionY1-positionY2));
            var dx = positionX1 - positionX2;
            var dy = positionY1 - positionY2;
            var summa = dx * dx + dy * dy;
            if (Math.Abs(summa) < tolerance)
            {
                return 0;
            }

            return (int)Math.Round(Math.Sqrt(summa));
        }
        //***************************************************************
        //Ориентация на местности: Проверка находится ли заданная точка в пределах досягаемости
        //public bool PosInRange(Npc npc, float targetX, float targetY, float targetZ, int distance)
        //***************************************************************
        public bool PosInRange(Npc npc, float targetX, float targetY, int distance)
        {
            return Delta(targetX, targetY, npc.Position.X, npc.Position.Y) <= distance;
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
        public int GetMinCheckPoint(Npc npc, List<string> pointsList)
        {
            int m, minDist;
            string s;

            var result = -1;
            minDist = -1;
            // проверка на наличие маршрута
            if (pointsList.Count == 0)
            {
                //_log.Warn("нет данных по маршруту.");
                //character.SendMessage("[MoveTo] нет данных по маршруту.");
                return -1;
            }

            for (var i = 0; i < pointsList.Count - 1; i++)
            {
                s = pointsList[i];
                Position.Y = ExtractValue(s, 2);
                Position.X = ExtractValue(s, 1);

                //_log.Warn(s + " x:=" + Position.X + " y:=" + Position.Y);

                m = Delta(Position.X, Position.Y, npc.Position.X, npc.Position.Y);

                if (m <= 0) { continue; }

                if (result == -1)
                {
                    minDist = m;
                    result = i;
                }
                else if (m < minDist)
                {
                    minDist = m;
                    result = i;
                }
            }
            return result;
        }
        //***************************************************************
        public void StartRecord(Simulation sim, Character ch)
        {
            if (SavePathEnabled) { return; }
            if (MoveToPathEnabled)
            {
                _log.Warn("while following the route, recording is not possible.");
                //character.SendMessage("[MoveTo] while following the route, recording is not possible.");
                return;
            }
            RecordPath.Clear();
            PointsCount = 0;
            _log.Warn("route recording started ...");
            //character.SendMessage("[MoveTo] route recording started ...");
            SavePathEnabled = true;
            RepeatTo(ch, MoveTrigerDelay, sim);
        }
        public void Record(Simulation sim, Character ch)
        {
            //if (!SavePathEnabled) { return; }
            var s = "|" + ch.Position.X + "|" + ch.Position.Y + "|" + ch.Position.Z + "|";
            RecordPath.Add(s);
            PointsCount++;
            _log.Warn("added checkpoint # {0}", PointsCount);
            //character.SendMessage("[MoveTo] добавлен чекпоинт #" + PointsCount);
            RepeatTo(ch, MoveTrigerDelay, sim);
        }

        //***************************************************************
        public void StopRecord(Simulation sim)
        {
            // записываем в файл
            using (StreamWriter sw = new StreamWriter(GetRecordFileName()))
            {
                foreach (var b in RecordPath)
                {
                    sw.WriteLine(b.ToString());
                }
            }
            _log.Warn("Route recording completed.");
            //character.SendMessage("[MoveTo] запись маршрута завершена.");
            SavePathEnabled = false;
        }
        //***************************************************************
        public string GetRecordFileName()
        {
            var result = RecordFilesPath + MoveFileName + RecordFileExt;
            return result;
        }
        public string GetMoveFileName()
        {
            var result = MoveFilesPath + MoveFileName + MoveFileExt;
            return result;
        }
        //***************************************************************
        public void ParseMoveClient(Npc npc)
        {
            if (!SavePathEnabled) { return; }
            Position.X = npc.Position.X;
            Position.Y = npc.Position.Y;
            Position.Z = npc.Position.Z;
            var s = "|" + Position.X + "|" + Position.Y + "|" + Position.Z + "|";
            RecordPath.Add(s);
            PointsCount++;
            //_log.Warn("добавлен чекпоинт # {0}", PointsCount);
            //character.SendMessage("[MoveTo] добавлен чекпоинт #" + PointsCount);
        }
        //***************************************************************
        public void GoToPath(Npc npc, bool ToForward)
        {
            MoveToPathEnabled = !MoveToPathEnabled;
            MoveToForward = ToForward;
            if (!MoveToPathEnabled)
            {
                //_log.Warn("следование по маршруту остановлено.");
                //character.SendMessage("[MoveTo] следование по маршруту остановлено.");
                StopMove(npc);
                return;
            }
            //предположительно путь уже прописан в MovePath
            //_log.Warn("пробуем выйти на путь...");
            //character.SendMessage("[MoveTo] пробуем выйти на путь...");
            //сперва идем к ближайшему чекпоинту
            var i = GetMinCheckPoint(npc, MovePath);
            if (i < 0)
            {
                //_log.Warn("чекпоинт не найден.");
                //character.SendMessage("[MoveTo] чекпоинт не найден.");
                StopMove(npc);
                return;
            }
            _log.Warn("found nearest checkpoint # "+ i +" run there ...");
            //character.SendMessage("[MoveTo] найден ближайший чекпоинт #" + i + " бежим туда...");
            MoveToPathEnabled = true;
            MoveStepIndex = i;
            //_log.Warn("checkpoint #" + i);
            //character.SendMessage("[MoveTo] checkpoint #" + i);
            var s = MovePath[MoveStepIndex];
            Position.X = ExtractValue(s, 1);
            Position.Y = ExtractValue(s, 2);
            Position.Z = ExtractValue(s, 3);
            if (Math.Abs(oldX - Position.X) > tolerance && Math.Abs(oldY - Position.Y) > tolerance && Math.Abs(oldZ - Position.Z) > tolerance)
            {
                oldX = Position.X;
                oldY = Position.Y;
                oldZ = Position.Z;
                oldTime = 0;
            }
            RepeatMove(this, npc, Position.X, Position.Y, Position.Z);
            chkTime = 0;
        }

        public void MoveTo(Simulation sim, Npc npc, float TargetX, float TargetY, float TargetZ)
        {
            if (Position == null)
            {
                StopMove(npc);
                return;
            }
            var move = false;
            var x = npc.Position.X - TargetX;
            var y = npc.Position.Y - TargetY;
            var z = npc.Position.Z - TargetZ;
            var MaxXYZ = Math.Max(Math.Max(Math.Abs(x), Math.Abs(y)), Math.Abs(z));
            float tempMovingDistance;

            if (runningMode)
            {
                MovingDistance = 0.5f;
            }
            else
            {
                MovingDistance = 0.25f;
            }


            if (Math.Abs(x) > RangeToCheckPoint)
            {
                if (Math.Abs(MaxXYZ - Math.Abs(x)) > tolerance)
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
                    npc.Position.X = Position.X;
                }
                move = true;
            }
            if (Math.Abs(y) > RangeToCheckPoint)
            {
                if (Math.Abs(MaxXYZ - Math.Abs(y)) > tolerance)
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
                    npc.Position.Y = Position.Y;
                }
                move = true;
            }
            if (Math.Abs(z) > RangeToCheckPoint)
            {
                if (Math.Abs(MaxXYZ - Math.Abs(z)) > tolerance)
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
                    npc.Position.Z = Position.Z;
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
            ////------------------взгляд_персонажа_будет(движение_куда<-движение_откуда)
            var angle = MathUtil.CalculateAngleFrom(npc.Position.X, npc.Position.Y, TargetX, TargetY);
            var rotZ = MathUtil.ConvertDegreeToDirection(angle);
            moveType.RotationX = 0;
            moveType.RotationY = 0;
            moveType.RotationZ = rotZ;
            if (runningMode)
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
            moveType.Time = (uint)Rand.Next(0, 10000);     // has to change all the time for normal motion.
            if (move)
            {
                // moving to the point #
                npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                RepeatMove(sim, npc, TargetX, TargetY, TargetZ);
            }
            else
            {
                OnMove(npc);
            }
        }
        public void RepeatMove(Simulation sim, Npc npc, float TargetX, float TargetY, float TargetZ, double time = 100)
        {
            //if ((sim ?? this).AbandonTo)
            {
                TaskManager.Instance.Schedule(new Move(sim ?? this, npc, TargetX, TargetY, TargetZ), TimeSpan.FromMilliseconds(time));
            }
        }

        public void RepeatTo(Character ch, double time = 1000, Simulation sim = null)
        {
            if ((sim ?? this).SavePathEnabled)
            {
                TaskManager.Instance.Schedule(new Record(sim ?? this, ch), TimeSpan.FromMilliseconds(time));
            }
        }

        //***************************************************************
        public void StopMove(Npc npc)
        {
            //_log.Warn("останавливаемся...");
            //character.SendMessage("[MoveTo] останавливаемся...");
            var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
            moveType.X = npc.Position.X;
            moveType.Y = npc.Position.Y;
            moveType.Z = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;
            var angle = MathUtil.CalculateAngleFrom(npc.Position.X, npc.Position.Y, Position.X, Position.Y);
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
            moveType.Time = (uint)Rand.Next(0, 10000); // has to change all the time for normal motion.
            npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
            MoveToPathEnabled = false;
        }
        public void PauseMove(Npc npc)
        {
            //_log.Warn("постоим немного...");
            //character.SendMessage("[MoveTo] постоим немного...");
            var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);
            moveType.X = npc.Position.X;
            moveType.Y = npc.Position.Y;
            moveType.Z = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;
            var angle = MathUtil.CalculateAngleFrom(npc.Position.X, npc.Position.Y, Position.X, Position.Y);
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
            moveType.Time = (uint)Rand.Next(0, 10000); // has to change all the time for normal motion.
            npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
        }

        public void OnMove(Npc npc)
        {
            if (!MoveToPathEnabled)
            {
                //_log.Warn("OnMove disabled");
                StopMove(npc);
                return;
            }
            try
            {
                MovePath.Count(); // проверяем на существование объекта, при отладке всякое может быть
            }
            catch
            {
                //_log.Warn("Error: {0}", e);
                //character.SendMessage("[MoveTo] Error: {0}", e);
                StopMove(npc);
                return;
            }
            var s = MovePath[MoveStepIndex];
            Position.X = ExtractValue(s, 1);
            Position.Y = ExtractValue(s, 2);
            Position.Z = ExtractValue(s, 3);
            if (!PosInRange(npc, Position.X, Position.Y, 3))
            {
                RepeatMove(this, npc, Position.X, Position.Y, Position.Z);
                return;
            }
            if (MoveToForward)
            {
                if (MoveStepIndex == MovePath.Count - 1)
                {
                    //_log.Warn("мы по идее в конечной точке.");
                    //character.SendMessage("[MoveTo] мы по идее в конечной точке.");
                    //StopMove(npc);
                    // сделаем паузу
                    PauseMove(npc);
                    MoveToForward = false; // разворачиваем назад
                    MoveStepIndex--;
                    //_log.Warn("walk to #" + MoveStepIndex);
                    //character.SendMessage("[MoveTo] бежим к #" + MoveStepIndex);
                    s = MovePath[MoveStepIndex];
                    Position.X = ExtractValue(s, 1);
                    Position.Y = ExtractValue(s, 2);
                    Position.Z = ExtractValue(s, 3);
                    RepeatMove(this, npc, Position.X, Position.Y, Position.Z, 20000);
                    return;
                }
                MoveStepIndex++;
                //_log.Warn("мы достигли чекпоинта идем далее...");
                //character.SendMessage("[MoveTo] мы достигли чекпоинта идем далее...");
            }
            else
            {
                if (MoveStepIndex > 0)
                {
                    MoveStepIndex--;
                    //_log.Warn("we reached checkpoint go further ...");
                    //character.SendMessage("[MoveTo] мы достигли чекпоинта идем далее...");
                }
                else
                {
                    //_log.Warn("мы по идее в начальной точке.");
                    //character.SendMessage("[MoveTo] мы по идее в начальной точке.");
                    //StopMove(npc);
                    // сделаем паузу
                    PauseMove(npc);
                    MoveToForward = true; // разворачиваем назад
                    MoveStepIndex++;
                    //_log.Warn("walk to #" + MoveStepIndex);
                    //character.SendMessage("[MoveTo] бежим к #" + MoveStepIndex);
                    s = MovePath[MoveStepIndex];
                    Position.X = ExtractValue(s, 1);
                    Position.Y = ExtractValue(s, 2);
                    Position.Z = ExtractValue(s, 3);
                    RepeatMove(this, npc, Position.X, Position.Y, Position.Z, 20000);
                    return;
                }
            }
            _log.Warn("walk to #" + MoveStepIndex);
            //character.SendMessage("[MoveTo] бежим к #" + MoveStepIndex);
            s = MovePath[MoveStepIndex];
            Position.X = ExtractValue(s, 1);
            Position.Y = ExtractValue(s, 2);
            Position.Z = ExtractValue(s, 3);
            RepeatMove(this, npc, Position.X, Position.Y, Position.Z);
        }

        public void Init(Unit unit) //Вызывается при включении скрипта
        {
            switch (unit)
            {
                case Character ch:
                    character = ch;
                    break;
                case Npc np:
                    npc = np;
                    break;
            }

            Position = new Point();
            RecordPath = new List<string>();
            //RecordPath = File.ReadLines(GetMoveFileName()).ToList();
        }

        public void ReadPath() //Вызывается при включении скрипта
        {
            try
            {
                MovePath = new List<string>();
                MovePath = File.ReadLines(GetMoveFileName()).ToList();
            }
            catch (Exception e)
            {
                _log.Warn("Error in read MovePath: {0}", e.Message);
                //character.SendMessage("[MoveTo] Error in read MovePath: {0}", e);
                StopMove(npc);
            }
            try
            {
                RecordPath = new List<string>();
                //RecordPath = File.ReadLines(GetMoveFileName()).ToList();
            }
            catch (Exception e)
            {
                _log.Warn("Error in read RecordPath: {0}", e.Message);
                //character.SendMessage("[MoveTo] Error in read MovePath: {0}", e);
                StopMove(npc);
            }
        }
    }
}
