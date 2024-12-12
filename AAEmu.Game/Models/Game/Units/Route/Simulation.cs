using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;
using AAEmu.Game.Models.Tasks.Npcs;
using AAEmu.Game.Models.Tasks.UnitMove;
using AAEmu.Game.Utils;

using NLog;

using static AAEmu.Game.Models.Game.Skills.SkillControllers.SkillController;

namespace AAEmu.Game.Models.Game.Units.Route;

/// <summary>
/// Control NPC to move along this route
/// </summary>
public class Simulation : Patrol
{
    public Simulation(GameObject unit)
    {
        Init(unit);
    }

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public Character Character { get; set; }
    public Npc Npc { get; set; }

    public NpcDeleteTask NpcDeleteTask { get; set; }

    public bool AbandonTo { get; set; } = false; // to interrupt repeat()
    public bool Cycle { get; set; } // for non-stop movement along the route
    public bool Remove { get; set; } // удалить Npc в конце движения по маршруту
    public Vector3 TargetPosition { get; set; } // Target position

    // movement data
    public Dictionary<string, List<Vector3>> Paths { get; set; } // available routes for Npc movement
    public List<Vector3> Path { get; set; }       //  points for moving along the route
    public List<string> MovePath { get; set; }    //  points for moving along the route
    public List<string> RecordPath { get; set; }  //  данные для записи пути
    public int PointsCount { get; set; }          // кол-во поинтов в процессе записи пути
    public bool SavePathEnabled { get; set; }     // флаг записи пути
    public bool MoveToPathEnabled { get; set; }   // флаг движения по пути
    public bool MoveToForward { get; set; }       // направление движения да - вперед, нет - назад
    public bool RunningMode { get; set; }         // режим движения да - бежать, нет - идти
    public int MoveStepIndex { get; set; }        // текущ. чекпоинт (куда бежим сейчас)
    private Vector3 OldPos { get; set; } = Vector3.Zero;
    //*******************************************************
    private const string RecordFilesPath = @"./Data/Path/"; // путь где хранятся наши файлы
    private const string RecordFileExt = @".path"; // расширение по умолчанию
    private const string MoveFilesPath = @"./Data/Path/"; // путь где хранятся наши файлы
    private const string MoveFileExt = @".path"; // расширение по умолчанию
    public string MoveFileName { get; set; } = string.Empty; // имя файла для маршрута
    public string MoveFileName2 { get; set; } = string.Empty; // имя файла для маршрута

    //Not used
    //private float MovingDistance = 0.25f; //0.3f;

    private float RangeToCheckPoint = 0.5f; // дистанция до чекпоинта при которой считается , что мы достигли оного
    private int MoveTrigerDelay = 1000;     // срабатывание таймера на движение  0,8 сек

    private uint SkillId { get; set; }
    private uint Timeout { get; set; }

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
       |15629.0|14989.02|141.2055|
       |15628.0|14987.24|141.3826|
       |15626.0|14983.88|141.3446|
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
    /// <summary>
    /// Ориентация на местности: Проверка находится ли заданная точка в пределах досягаемости
    /// </summary>
    /// <param name="npc"></param>
    /// <param name="target"></param>
    /// <param name="distance"></param>
    /// <returns></returns>
    public static bool PosInRange(Npc npc, Vector3 target, int distance)
    {
        return MathUtil.CalculateDistance(npc.Transform.World.Position, target, true) <= distance;
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
    public static float ExtractValue(string sData, int nIndex)
    {
        System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
        int i;
        var j = 0;
        var s = sData;
        while (j < nIndex)
        {
            i = s.IndexOf('|');
            if (i >= 0)
            {
                s = s.Substring(i + 1);
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
            s = s.Substring(0, i);
        }
        var result = Convert.ToSingle(s);
        return result;
    }
    //***************************************************************
    public int GetMinCheckPoint(Npc npc, List<Vector3> pointsList)
    {
        var result = -1;
        float minDist = -1;
        // check for a route
        if (pointsList == null || pointsList.Count == 0)
        {
            //Logger.Warn("no route data...");
            //Character.SendMessage("[MoveTo] no route data...");
            return -1;
        }

        for (var i = 0; i < pointsList.Count - 1; i++)
        {
            TargetPosition = pointsList[i];

            //Logger.Warn($"Проверяем точку #{i} с координатами  x={TargetPosition.World.Position.X} y={TargetPosition.World.Position.Y}");

            var m = MathUtil.CalculateDistance(TargetPosition, npc.Transform.World.Position, true);
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
            //Logger.Warn("while following the route, recording is not possible.");
            //Character.SendMessage("[MoveTo] while following the route, recording is not possible.");
            return;
        }
        RecordPath.Clear();
        PointsCount = 0;
        //Logger.Warn("route recording started ...");
        //Character.SendMessage("[MoveTo] route recording started ...");
        SavePathEnabled = true;
        RepeatTo(ch, MoveTrigerDelay, sim);
    }
    public void Record(Simulation sim, Character ch)
    {
        var s = $"|{ch.Transform.World.Position.X}|{ch.Transform.World.Position.Y}|{ch.Transform.World.Position.Z}|";
        s = s.Replace(",", ".");
        RecordPath.Add(s);
        PointsCount++;
        //Logger.Warn($"added checkpoint #{PointsCount}");
        //Character.SendMessage("[MoveTo] добавлен чекпоинт #" + PointsCount);
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
                sw.WriteLine(b);
            }
        }
        //Logger.Warn("Route recording completed.");
        //Character.SendMessage("[MoveTo] запись маршрута завершена.");
        SavePathEnabled = false;
    }
    //***************************************************************

    private string GetRecordFileName()
    {
        var result = System.IO.Path.Combine(RecordFilesPath, MoveFileName + RecordFileExt);
        return result;
    }

    private string GetMoveFileName()
    {
        var result = System.IO.Path.Combine(MoveFilesPath, MoveFileName + MoveFileExt);
        return result;
    }

    //***************************************************************
    public void ParseMoveClient(Npc npc)
    {
        if (!SavePathEnabled) { return; }
        TargetPosition = new Vector3(npc.Transform.World.Position.X, npc.Transform.World.Position.Y, npc.Transform.World.Position.Z);
        var s = "|" + TargetPosition.X + "|" + TargetPosition.Y + "|" + TargetPosition.Z + "|";
        RecordPath.Add(s);
        PointsCount++;
        //Logger.Warn("добавлен чекпоинт # {0}", PointsCount);
        //Character.SendMessage("[MoveTo] добавлен чекпоинт #" + PointsCount);
    }
    //***************************************************************
    public void GoToPath(Npc npc, bool toForward, uint skillId = 0, uint timeout = 0)
    {
        MoveToPathEnabled = !MoveToPathEnabled;
        MoveToForward = toForward;
        if (!MoveToPathEnabled)
        {
            //Logger.Warn("the route is stopped...");
            //Character.SendMessage("[MoveTo] the route is stopped...");
            StopMove(npc);
            return;
        }

        ReadPath();

        SkillId = skillId;
        Timeout = timeout;

        // presumably the path is already registered in MovePath
        //Logger.Warn("trying to get on the path...");
        //Character.SendMessage("[MoveTo] trying to get on the path...");
        // first go to the closest checkpoint
        npc.BroadcastPacket(new SCUnitModelPostureChangedPacket(npc, npc.AnimActionId, false), true);
        Path = GetPaths(MoveFileName);

        if (Path.Count == 0)
        {
            Logger.Warn($"Stop moving... Информация о пути MoveFileName={MoveFileName} отсутствует!");
            return;
        }

        var i = GetMinCheckPoint(npc, Path);
        if (i < 0)
        {
            //Logger.Warn("checkpoint not found...");
            //Character.SendMessage("[MoveTo] checkpoint not found...");
            StopMove(npc);
            return;
        }
        //Logger.Warn($"found nearest checkpoint #{i} run there ...");
        //Character.SendMessage($"[MoveTo] found nearest checkpoint #{i} run there ...");
        MoveToPathEnabled = true;
        MoveStepIndex = i;
        //Logger.Warn($"checkpoint #{i}");
        //Character.SendMessage($"[MoveTo] checkpoint #{i}");
        var s = MovePath[MoveStepIndex];
        TargetPosition = new Vector3(ExtractValue(s, 1), ExtractValue(s, 2), ExtractValue(s, 3));
        if (Math.Abs(OldPos.X - TargetPosition.X) > tolerance && Math.Abs(OldPos.Y - TargetPosition.Y) > tolerance && Math.Abs(OldPos.Z - TargetPosition.Z) > tolerance)
        {
            OldPos = new Vector3(TargetPosition.X, TargetPosition.Y, TargetPosition.Z);
        }
        RepeatMove(this, npc, TargetPosition);
    }
    public void GoToPath2(Npc npc, bool toForward, uint skillId = 0, uint timeout = 0)
    {
        MoveToForward = toForward;

        ReadPath();

        SkillId = skillId;
        Timeout = timeout;

        // presumably the path is already registered in MovePath
        //Logger.Warn("trying to get on the path...");
        //Character.SendMessage("[MoveTo] trying to get on the path...");
        // first go to the closest checkpoint
        npc.BroadcastPacket(new SCUnitModelPostureChangedPacket(npc, npc.AnimActionId, false), true);
        Path = GetPaths(MoveFileName);

        if (Path.Count == 0)
        {
            Logger.Warn($"Stop moving... Информация о пути MoveFileName={MoveFileName} отсутствует!");
            return;
        }

        var i = GetMinCheckPoint(npc, Path);
        if (i < 0)
        {
            //Logger.Warn("checkpoint not found...");
            //Character.SendMessage("[MoveTo] checkpoint not found...");
            StopMove(npc);
            return;
        }
        //Logger.Warn($"found nearest checkpoint #{i} run there ...");
        //Character.SendMessage($"[MoveTo] found nearest checkpoint #{i} run there ...");
        MoveToPathEnabled = true;
        MoveStepIndex = i;
        //Logger.Warn($"checkpoint #{i}");
        //Character.SendMessage($"[MoveTo] checkpoint #{i}");
        var s = MovePath[MoveStepIndex];
        TargetPosition = new Vector3(ExtractValue(s, 1), ExtractValue(s, 2), ExtractValue(s, 3));
        if (Math.Abs(OldPos.X - TargetPosition.X) > tolerance && Math.Abs(OldPos.Y - TargetPosition.Y) > tolerance && Math.Abs(OldPos.Z - TargetPosition.Z) > tolerance)
        {
            OldPos = new Vector3(TargetPosition.X, TargetPosition.Y, TargetPosition.Z);
        }
        RepeatMove(this, npc, TargetPosition, timeout);
    }

    public void MoveTo(Simulation sim, Npc npc, Vector3 target)
    {
        var move = false;
        var distance = npc.BaseMoveSpeed * (100 / 1000.0f);
        distance *= npc.MoveSpeedMul; // Apply speed modifier
        if (distance < 0.01f)
        {
            // take the next point to move to it
            OnMove(npc);
            return;
        }

        var dist = MathUtil.CalculateDistance(npc.Transform.World.Position, target, true);
        if (dist > RangeToCheckPoint)
        {
            move = true;
        }
        if (move)
        {
            // moving to the point #
            if (npc.ActiveSkillController != null && npc.ActiveSkillController.State != SCState.Ended)
            {
                return;
            }

            var oldPosition = npc.Transform.Local.ClonePosition();

            var targetDist = MathUtil.CalculateDistance(npc.Transform.Local.Position, target, true);
            if (targetDist <= 0.05f)
            {
                return;
            }

            var moveType = (UnitMoveType)MoveType.GetType(MoveTypeEnum.Unit);

            var travelDist = Math.Min(targetDist, distance);

            // TODO: Implement proper use for Transform.World.AddDistanceToFront)
            var (newX, newY, _) = PositionAndRotation.AddDistanceToFront(travelDist, targetDist, npc.Transform.Local.Position, target);

            var newZ = WorldManager.Instance.GetHeight(npc.Transform.ZoneId, npc.Transform.World.Position.X, npc.Transform.World.Position.Y);
            if (newZ == 0)
            {
                newZ = npc.Transform.World.Position.Z;
            }

            npc.Transform.Local.SetPosition(newX, newY, newZ);

            var angle = MathUtil.CalculateAngleFrom(npc.Transform.Local.Position, target);
            var (velX, velY) = MathUtil.AddDistanceToFront(4000, 0, 0, (float)angle.DegToRad());
            npc.Transform.Local.SetRotationDegree(0f, 0f, (float)angle - 90);
            var (rx, ry, rz) = npc.Transform.Local.ToRollPitchYawSBytesMovement();

            moveType.X = npc.Transform.Local.Position.X;
            moveType.Y = npc.Transform.Local.Position.Y;
            moveType.Z = npc.Transform.Local.Position.Z;
            moveType.VelX = (short)velX;
            moveType.VelY = (short)velY;
            moveType.RotationX = rx;
            moveType.RotationY = ry;
            moveType.RotationZ = rz;
            moveType.ActorFlags = (byte)(RunningMode ? 4 : 5); // 5-walk, 4-run, 3-stand still
            moveType.Flags = 0;

            moveType.DeltaMovement = new sbyte[3];
            moveType.DeltaMovement[0] = 0;
            moveType.DeltaMovement[1] = (sbyte)(RunningMode ? 127 : 63);
            moveType.DeltaMovement[2] = 0;
            moveType.Stance = npc.CurrentGameStance;    // COMBAT = 0x0, IDLE = 0x1
            moveType.Alertness = npc.CurrentAlertness; // IDLE = 0x0, ALERT = 0x1, COMBAT = 0x2
            moveType.Time = (uint)(DateTime.UtcNow - DateTime.UtcNow.Date).TotalMilliseconds;

            npc.CheckMovedPosition(oldPosition);

            npc.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
            RepeatMove(sim, npc, target);
        }
        else
        {
            // take the next point to move to it
            OnMove(npc);
        }
    }

    /* unused private void RepeatMove(Simulation sim, Npc npc, float targetX, float TargetY, float TargetZ, double time = 100)
    {
        //if ((sim ?? this).AbandonTo)
        {
            TaskManager.Instance.Schedule(new Move(sim ?? this, npc, targetX, TargetY, TargetZ), TimeSpan.FromMilliseconds(time));
        }
    }*/

    private void RepeatMove(Simulation sim, Npc npc, Vector3 target, double time = 100)
    {
        TaskManager.Instance.Schedule(new Move(sim ?? this, npc, target.X, target.Y, target.Z), TimeSpan.FromMilliseconds(time));
    }

    private void RepeatTo(Character ch, double time = 1000, Simulation sim = null)
    {
        if ((sim ?? this).SavePathEnabled)
        {
            TaskManager.Instance.Schedule(new Record(sim ?? this, ch), TimeSpan.FromMilliseconds(time));
        }
    }

    //***************************************************************
    public void StopMove(Npc npc)
    {
        //Logger.Warn("stop moving...");
        //Character.SendMessage("[MoveTo] stop moving ...");
        npc.StopMovement();
        MoveToPathEnabled = false;
        npc.IsInPatrol = false;

        if (Remove)
        {
            if (Timeout > 0)
            {
                NpcDeleteTask = new NpcDeleteTask(npc);
                TaskManager.Instance.Schedule(NpcDeleteTask, TimeSpan.FromMilliseconds(Timeout * 1000));
                return;
            }

            if (NpcDeleteTask == null)
            {
                npc.Spawner.DespawnWithRespawn(npc);
            }
        }
    }

    public static void PauseMove(Npc npc)
    {
        //Logger.Warn("let's stand a little...");
        //Character.SendMessage("[MoveTo] let's stand a little...");
        npc.StopMovement();
    }

    public void OnMove(Npc npc)
    {
        if (!MoveToPathEnabled)
        {
            //Logger.Warn("OnMove disabled");
            StopMove(npc);
            return;
        }
        if (MovePath == null || MovePath.Count == 0)
        {
            //Logger.Warn("Error: Path data is missing");
            //Character.SendMessage("[MoveTo]Error: Path data is missing);
            StopMove(npc);
            return;
        }
        TargetPosition = Path[MoveStepIndex];

        if (!PosInRange(npc, TargetPosition, 3))
        {
            RepeatMove(this, npc, TargetPosition);
            return;
        }
        if (MoveToForward)
        {
            if (MoveStepIndex == MovePath.Count - 1)
            {
                //Logger.Warn("we are at the end point...");
                //Character.SendMessage("[MoveTo] we are at the end point...");
                MoveToForward = false; // turn back
                MoveStepIndex--;
                //Logger.Warn($"walk to #{MoveStepIndex}");
                //Character.SendMessage("[MoveTo] бежим к #" + MoveStepIndex);
                TargetPosition = Path[MoveStepIndex];
                if (Cycle)
                {
                    // let's pause, use skill
                    PauseMove(npc);
                    var time = 100.0;
                    if (SkillId > 0)
                    {
                        npc.UseSkill(SkillId, npc);
                        var useSkill = new Skill(SkillManager.Instance.GetSkillTemplate(SkillId));
                        if (Timeout > 0)
                        {
                            time = Timeout * 1000;
                        }
                        else if (useSkill.Template.CastingTime != 0 && useSkill.Template.CooldownTime != 0)
                        {
                            time = useSkill.Template.CastingTime + useSkill.Template.CooldownTime;
                        }

                        SkillId = 0;
                        //Timeout = 0;
                    }

                    RepeatMove(this, npc, TargetPosition, time);
                }
                else if (!string.IsNullOrEmpty(MoveFileName2))
                {
                    // let's pause, use skill
                    PauseMove(npc);
                    var time = Timeout * 1000;

                    MoveFileName = MoveFileName2; // заменим на путь назад
                    GoToPath2(npc, true, 0, time);
                }
                else
                {

                    StopMove(npc);
                }
                return;
            }
            MoveStepIndex++;
            //Logger.Warn("we have reached checkpoint go on...");
            //Character.SendMessage("[MoveTo] we have reached checkpoint go on...");
        }
        else
        {
            if (MoveStepIndex > 0)
            {
                MoveStepIndex--;
                //Logger.Warn("we reached checkpoint go further...");
                //Character.SendMessage("[MoveTo] we reached checkpoint go further...");
            }
            else
            {
                //Logger.Warn("we are at the starting point...");
                //Character.SendMessage("[MoveTo] we are at the starting point...");
                MoveToForward = true; // turn back
                MoveStepIndex++;
                //Logger.Warn($"walk to #{MoveStepIndex}");
                //Character.SendMessage("[MoveTo] walk to #{MoveStepIndex});
                TargetPosition = Path[MoveStepIndex];
                if (Cycle)
                {
                    // let's pause, use skill
                    PauseMove(npc);
                    var time = 100.0;
                    if (SkillId > 0)
                    {
                        npc.UseSkill(SkillId, npc);
                        var useSkill = new Skill(SkillManager.Instance.GetSkillTemplate(SkillId));
                        if (Timeout > 0)
                        {
                            time = Timeout * 1000;
                        }
                        else if (useSkill.Template.CastingTime != 0 && useSkill.Template.CooldownTime != 0)
                        {
                            time = useSkill.Template.CastingTime + useSkill.Template.CooldownTime;
                        }

                        SkillId = 0;
                        //Timeout = 0;
                    }

                    RepeatMove(this, npc, TargetPosition, time);
                }
                else
                {
                    StopMove(npc);
                }
                return;
            }
        }
        //Logger.Warn($"walk to #{MoveStepIndex}");
        //Character.SendMessage("[MoveTo] walk to #{MoveStepIndex});
        TargetPosition = Path[MoveStepIndex];
        RepeatMove(this, npc, TargetPosition);
    }

    private void Init(GameObject unit) // Called when the script is enabled
    {
        switch (unit)
        {
            case Character ch:
                Character = ch;
                break;
            case Npc np:
                Npc = np;
                break;
        }

        TargetPosition = unit.ParentObj?.Transform?.World?.Position ?? Vector3.Zero;
        RecordPath = [];
        Paths = [];
    }

    private void ReadPath() // Called when the script is enabled
    {
        // Read MovePath
        try
        {
            if (Paths.ContainsKey(MoveFileName))
            {
                return;
            }

            MovePath = [];
            var pathFileName = GetMoveFileName();
            if (File.Exists(pathFileName))
            {
                MovePath = File.ReadLines(pathFileName).ToList();
            }
            else
            {
                if (Npc != null)
                    Logger.Debug($"Missing path file {pathFileName} for NPC {Npc.Name}, TemplateId: {Npc.TemplateId}, ObjId:{Npc.ObjId}");
                if (Character != null)
                    Logger.Debug($"Missing path file {pathFileName} for Character {Character.Name}, ObjId:{Character.ObjId}");
            }

            AddPaths();
        }
        catch (Exception e)
        {
            Logger.Warn($"Error in read MovePath: {e.Message}");
            StopMove(Npc);
        }

        // Read RecordPath
        try
        {
            RecordPath = [];
            //RecordPath = File.ReadLines(GetMoveFileName()).ToList();
        }
        catch (Exception e)
        {
            Logger.Warn($"Error in read RecordPath: {e.Message}");
            //Character.SendMessage($"[MoveTo] Error in read MovePath: {e.Message}");
            StopMove(Npc);
        }
    }

    private void AddPaths()
    {
        var route = new List<Vector3>();
        foreach (var s in MovePath)
        {
            var xyz = new Vector3(ExtractValue(s, 1), ExtractValue(s, 2), ExtractValue(s, 3));
            route.Add(xyz);
        }

        Paths.TryAdd(MoveFileName, route);
    }

    private List<Vector3> GetPaths(string moveFileName)
    {
        var route = new List<Vector3>();
        if (Paths.TryGetValue(moveFileName, out var path))
        {
            route = path;
        }

        return route;
    }
}
