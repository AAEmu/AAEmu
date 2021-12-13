﻿using System.IO;
using AAEmu.Commons.IO;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Team;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Json;

using Newtonsoft.Json;

using NLog;

namespace AAEmu.Game.Scripts.Commands
{
    public class SaveSpawn : ICommand
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();
        public void OnLoad()
        {
            string[] name = { "savespawn" };
            CommandManager.Instance.Register( "savespawn", this );
        }

        public string GetCommandLineHelp()
        {
            return "(target)";
        }

        public string GetCommandHelpText()
        {
            return "write's npc's current position / rotation to a new json";
        }

        public void Execute( Character character, string[] args )
        {
            if ( character.CurrentTarget != null && character.CurrentTarget != character )
            {
                if ( character.CurrentTarget is Npc npc )
                {
                    string path = ( $"{FileManager.AppPath}Data/new_npc_spawns.json" );

                    if ( !File.Exists( path ) )
                        File.CreateText( path );

                    var spawner = new JsonNpcSpawns();
                    spawner.Position = new Pos();
                    spawner.Position.X = npc.Transform.Local.Position.X;
                    spawner.Position.Y = npc.Transform.Local.Position.Y;
                    spawner.Position.Z = npc.Transform.Local.Position.Z;
                    var (roll, pitch, yaw) = npc.Transform.Local.ToRollPitchYawSBytes();
                    spawner.Position.RotationX = roll;
                    spawner.Position.RotationY = pitch;
                    spawner.Position.RotationZ = yaw;
                    spawner.Count = 1;
                    spawner.UnitId = npc.TemplateId;
                    spawner.Scale = 0f;

                    string json = JsonConvert.SerializeObject( spawner, Formatting.Indented );
                    File.AppendAllText( path, json );

                    character.SendMessage( "[SaveSpawn] ObjId: {0} has been saved!", npc.ObjId );
                }
            }
        }
    }
}