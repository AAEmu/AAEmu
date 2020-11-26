using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using AAEmu.Commons.Utils;
using NLog;

namespace AAEmu.Game.Core.Managers
{
    public class TickManager : Singleton<TickManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        public delegate void OnTickEvent();
        public event OnTickEvent OnTick = delegate { };

        private void TickLoop()
        {
            while(true)
            {
                OnTick();
                Thread.Sleep(200);
            }
        }

        public void Initialize()
        {
            var TickThread = new Thread(TickLoop);
            TickThread.Start();
        }
    }
}
