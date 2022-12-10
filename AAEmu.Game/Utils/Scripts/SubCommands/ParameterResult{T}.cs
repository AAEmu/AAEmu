namespace AAEmu.Game.Utils.Scripts.SubCommands
{
    public class ParameterResult<T> : ParameterResult
    {
        public ParameterResult(string name, T value, string invalidMessage = null) : base(name, value, invalidMessage)
        {}
    }
}
