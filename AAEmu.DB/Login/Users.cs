using System;
using System.Collections.Generic;

namespace AAEmu.DB.Login
{
    public partial class Users
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Email { get; set; }
        public long LastLogin { get; set; }
        public string LastIp { get; set; }
        public long CreatedAt { get; set; }
        public long UpdatedAt { get; set; }
    }
}
