using NLog;

namespace AAEmu.Commons.Network
{
    public abstract class PacketMarshaler
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();

        public virtual void Read(PacketStream stream) 
        {
            _log.Warn("{0} doesn't inherit Read()", GetType().FullName);
        }

        public virtual PacketStream Write(PacketStream stream)
        {
            _log.Warn("{0} doesn't inherit Write()", GetType().FullName);
            return stream;
        }

        public virtual string Verbose()
        {
            return string.Empty;
        }
    }
}
