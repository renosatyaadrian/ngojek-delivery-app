﻿namespace DriverService.Dtos
{
    public class UserForCreateDto
    {
        public string Username { get; set; }
        public string Password { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public int PhoneNumber { get; set; }
        public double Balance { get; set; }
        public bool Blocked { get; set; }
    }
}
