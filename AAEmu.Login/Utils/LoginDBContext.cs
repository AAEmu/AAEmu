using AAEmu.DB.Login;
using AAEmu.Login.Models;

namespace AAEmu.Login.Utils
{
    internal class LoginDBContext : LoginContext
    {
        public LoginDBContext() : base(AppConfiguration.Instance.Database.ConnectionString) { }
    }
}
