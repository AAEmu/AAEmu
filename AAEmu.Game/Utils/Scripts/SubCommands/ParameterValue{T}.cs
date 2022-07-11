namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public class ParameterValue<T> : ParameterValue
    {
        public ParameterValue(string name, T value, string invalidMessage = null) : base(name, value, invalidMessage)
        {}

        public T GetValue()
        {
            return (T)Value;
        }
    }
}
