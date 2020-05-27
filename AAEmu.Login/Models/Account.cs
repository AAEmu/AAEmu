﻿using System;

 namespace AAEmu.Login.Models
{
    public class Account
    {
        public ulong Id { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Email { get; set; }
        public DateTime LastLogin { get; set; }
        public string LastIp { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public byte Level { get; set; }
    }
}
