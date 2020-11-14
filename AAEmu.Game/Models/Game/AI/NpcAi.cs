using System;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.AI.Abstracts;
using AAEmu.Game.Models.Game.AI.Static;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;
using AAEmu.Game.Models.Game.Units.Route;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;

/*
   Author:Sagara, NLObP
*/
namespace AAEmu.Game.Models.Game.AI
{
    public sealed class NpcAi : ACreatureAi
    {
        public double Angle { get; set; }
        //public float Distance { get; set; } = 0f;
        //public float movingDistance { get; set; } = 0.27f;
        //public double AngleTmp { get; set; }
        //public float AngVelocity { get; set; } = 45.0f;
        //public float MaxVelocityForward { get; set; } = 5.4f;
        //public float MaxVelocityBackward { get; set; } = 0f;
        //public float VelAccel { get; set; } = 1.8f;
        //public Vector3 vMovingDistance { get; set; } = new Vector3();
        //public Vector3 vMaxVelocityForwardRun { get; set; } = new Vector3(5.4f, 5.4f, 5.4f);
        //public Vector3 vMaxVelocityForwardWalk { get; set; } = new Vector3(1.8f, 1.8f, 1.8f);
        public Vector3 Velocity { get; set; } = new Vector3();
        //public Vector3 vMaxVelocityBackward { get; set; } = new Vector3();
        //public Vector3 vVelAccel { get; set; } = new Vector3(1.8f, 1.8f, 1.8f);
        //public Vector3 vPosition { get; set; } = new Vector3();
        //public Vector3 vTarget { get; set; } = new Vector3();
        //public Vector3 vDistance { get; set; } = new Vector3();
        //public float RangeToCheckPoint { get; set; } = 1.0f; // distance to checkpoint at which it is considered that we have reached it
        //public Vector3 vRangeToCheckPoint { get; set; } = new Vector3(1.0f, 1.0f, 0f); // distance to checkpoint at which it is considered that we have reached it

        public NpcAi(GameObject owner, float visibleRange) : base(owner, visibleRange)
        {
        }

        protected override void IamSeeSomeone(GameObject someone)
        {
            switch (someone.UnitType)
            {
                case BaseUnitType.Character:
                    var chr = (Character)someone;
                    var npc = (Npc)Owner;
                    var target = (BaseUnit)someone;
                    if (!npc.IsInBattle && npc.Hp > 0)
                    {
                        // Monstrosity & Hostile
                        if (npc.Faction.Id == 115 || npc.Faction.Id == 3)
                        {
                            // if the Npc is aggressive, he will look at us and attack if close to us, otherwise he just looks at us
                            if (npc.Template.Aggression && npc.Template.AggroLinkHelpDist * npc.Template.AttackStartRangeScale > Math.Abs(MathUtil.CalculateDistance(npc.Position, chr.Position, true)))
                            {
                                // NPC attacking us
                                //npc.Patrol = null;
                                //npc.Patrol?.Pause(npc);

                                // AiAggro(ai_commands = 4065, count=0)
                                chr.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, chr.ObjId), true);
                                chr.BroadcastPacket(new SCAiAggroPacket(npc.ObjId, 1, chr.ObjId), true);
                                chr.BroadcastPacket(new SCCombatEngagedPacket(npc.ObjId), true); // caster
                                chr.BroadcastPacket(new SCAggroTargetChangedPacket(npc.ObjId, chr.ObjId), true);
                                npc.CurrentTarget = target;
                                npc.SetForceAttack(true);
                                npc.IsAutoAttack = true;
                                npc.IsInBattle = true;
                                var combat = new Combat();
                                //npc.Patrol.UpdateTime = DateTime.Now;
                                combat.Execute(npc);
                            }
                            else if (Math.Abs(MathUtil.CalculateDistance(npc.Position, chr.Position, true)) < 10f) // preferredCombatDistance = 20
                            {
                                //npc.Patrol = null;
                                //npc.Patrol?.Pause(npc);

                                // Npc looks at us
                                if (npc.CurrentTarget != target)
                                {
                                    chr.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, chr.ObjId), true);
                                    npc.CurrentTarget = target;
                                }
                                var seq = (uint)(DateTime.Now - GameService.StartTime).TotalMilliseconds;
                                var moveType = (ActorData)UnitMovement.GetType(UnitMovementType.Actor);

                                moveType.X = npc.Position.X;
                                moveType.Y = npc.Position.Y;
                                //moveType.Z = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;
                                moveType.Z = npc.Position.Z;

                                // looks in the direction of movement
                                Angle = MathUtil.CalculateAngleFrom(npc, chr);
                                var rotZ = MathUtil.ConvertDegreeToDirection(Angle);
                                moveType.Rot = new Quaternion(0f, 0f, Helpers.ConvertDirectionToRadian(rotZ), 1f);
                                npc.Rot = moveType.Rot;

                                moveType.DeltaMovement = Vector3.Zero;
                                Velocity = Vector3.Zero;

                                moveType.Stance = 1;    //combat=0, idle=1
                                moveType.Alertness = 1; //idle=0, alert = 1, combat=2
                                moveType.Time = seq;
                                chr.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                            }
                            else if (npc.Template.AiFileId == AiFilesType.Roaming || npc.Template.AiFileId == AiFilesType.BigMonsterRoaming || npc.Template.AiFileId == AiFilesType.ArcherRoaming || npc.Template.AiFileId == AiFilesType.WildBoarRoaming)
                            {   // Npc roams around the spawn point in random directions
                                if (npc.CurrentTarget != null)
                                {
                                    chr.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, 0), true);
                                    npc.CurrentTarget = null;
                                }
                                if (npc.Patrol == null)
                                {
                                    npc.IsInBattle = false;
                                    npc.Patrol = new Roaming { Interrupt = true, Loop = true, Abandon = false };
                                    npc.Patrol.Interrupt = true; // можно прервать
                                    npc.Patrol.Loop = true;      // повторять в цикле
                                    npc.Patrol.Abandon = false;  // не прерванный
                                    npc.Patrol.Pause(npc);       // запишем точку останова в PausePosition
                                    npc.Patrol.LastPatrol = null; // предыдущего патруля нет
                                    npc.Patrol.Recovery(npc);     // запустим патруль
                                }
                                else
                                {
                                    npc.Patrol.Recovery(npc);
                                }
                            }

                        }
                    }
                    break;
                case BaseUnitType.Npc:
                    break;
                case BaseUnitType.Slave:
                    break;
                case BaseUnitType.Housing:
                    break;
                case BaseUnitType.Transfer:
                    break;
                case BaseUnitType.Mate:
                    break;
                case BaseUnitType.Shipyard:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        protected override void IamUnseeSomeone(GameObject someone)
        {
        }

        protected override void SomeoneSeeMe(GameObject someone)
        {
        }

        protected override void SomeoneUnseeMee(GameObject someone)
        {
        }

        protected override void SomeoneThatIamSeeWasMoved(GameObject someone, MovementAction action)
        {
            switch (someone.UnitType)
            {
                case BaseUnitType.Character:
                    var chr = (Character)someone;
                    var npc = (Npc)Owner;
                    var target = (BaseUnit)someone;
                    if (!npc.IsInBattle && npc.Hp > 0)
                    {
                        // Monstrosity & Hostile
                        if (npc.Faction.Id == 115 || npc.Faction.Id == 3)
                        {
                            // if the Npc is aggressive, he will look at us and attack if close to us, otherwise he just looks at us
                            if (npc.Template.Aggression && npc.Template.AggroLinkHelpDist * npc.Template.AttackStartRangeScale > Math.Abs(MathUtil.CalculateDistance(npc.Position, chr.Position, true)))
                            {
                                // NPC attacking us
                                //npc.Patrol = null;
                                //npc.Patrol?.Pause(npc);

                                // AiAggro(ai_commands = 4065, count=0)
                                chr.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, chr.ObjId), true);
                                chr.BroadcastPacket(new SCAiAggroPacket(npc.ObjId, 1, chr.ObjId), true);
                                chr.BroadcastPacket(new SCCombatEngagedPacket(npc.ObjId), true); // caster
                                chr.BroadcastPacket(new SCAggroTargetChangedPacket(npc.ObjId, chr.ObjId), true);
                                npc.CurrentTarget = target;
                                npc.SetForceAttack(true);
                                npc.IsAutoAttack = true;
                                npc.IsInBattle = true;
                                var combat = new Combat();
                                //npc.Patrol.UpdateTime = DateTime.Now;
                                combat.Execute(npc);
                            }
                            else if (Math.Abs(MathUtil.CalculateDistance(npc.Position, chr.Position, true)) < 10f) // preferredCombatDistance = 20
                            {
                                //npc.Patrol = null;
                                //npc.Patrol?.Pause(npc);

                                // Npc looks at us
                                if (npc.CurrentTarget != target)
                                {
                                    chr.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, chr.ObjId), true);
                                    npc.CurrentTarget = target;
                                }
                                var seq = (uint)(DateTime.Now - GameService.StartTime).TotalMilliseconds;
                                var moveType = (ActorData)UnitMovement.GetType(UnitMovementType.Actor);

                                moveType.X = npc.Position.X;
                                moveType.Y = npc.Position.Y;
                                //moveType.Z = AppConfiguration.Instance.HeightMapsEnable ? WorldManager.Instance.GetHeight(npc.Position.ZoneId, npc.Position.X, npc.Position.Y) : npc.Position.Z;
                                moveType.Z = npc.Position.Z;

                                // looks in the direction of movement
                                Angle = MathUtil.CalculateAngleFrom(npc, chr);
                                var rotZ = MathUtil.ConvertDegreeToDirection(Angle);
                                moveType.Rot = new Quaternion(0f, 0f, Helpers.ConvertDirectionToRadian(rotZ), 1f);
                                npc.Rot = moveType.Rot;

                                moveType.DeltaMovement = Vector3.Zero;
                                Velocity = Vector3.Zero;

                                moveType.Stance = 1;    //combat=0, idle=1
                                moveType.Alertness = 1; //idle=0, alert = 1, combat=2
                                moveType.Time = seq;
                                chr.BroadcastPacket(new SCOneUnitMovementPacket(npc.ObjId, moveType), true);
                            }
                            else if (npc.Template.AiFileId == AiFilesType.Roaming || npc.Template.AiFileId == AiFilesType.BigMonsterRoaming || npc.Template.AiFileId == AiFilesType.ArcherRoaming || npc.Template.AiFileId == AiFilesType.WildBoarRoaming)
                            {   // Npc roams around the spawn point in random directions
                                if (npc.CurrentTarget != null)
                                {
                                    chr.BroadcastPacket(new SCTargetChangedPacket(npc.ObjId, 0), true);
                                    npc.CurrentTarget = null;
                                }
                                if (npc.Patrol == null)
                                {
                                    npc.IsInBattle = false;
                                    npc.Patrol = new Roaming { Interrupt = true, Loop = true, Abandon = false };
                                    npc.Patrol.Interrupt = true; // можно прервать
                                    npc.Patrol.Loop = true;      // повторять в цикле
                                    npc.Patrol.Abandon = false;  // не прерванный
                                    npc.Patrol.Pause(npc);
                                    npc.Patrol.LastPatrol = null; // предыдущего патруля нет
                                    npc.Patrol.Recovery(npc);     // запустим патруль
                                }
                                else
                                {
                                    npc.Patrol.Recovery(npc);
                                }
                            }

                        }
                    }
                    break;
                case BaseUnitType.Npc:
                    break;
                case BaseUnitType.Slave:
                    break;
                case BaseUnitType.Housing:
                    break;
                case BaseUnitType.Transfer:
                    break;
                case BaseUnitType.Mate:
                    break;
                case BaseUnitType.Shipyard:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        protected override void SomeoneThatSeeMeWasMoved(GameObject someone, MovementAction action)
        {
        }
    }
}
