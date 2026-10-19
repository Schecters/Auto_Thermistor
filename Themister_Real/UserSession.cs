namespace Themister_Real
{
    public static class UserSession
    {
        public static string EmployeeId { get; set; } = "";
        public static string Name { get; set; } = "";
        public static string Role { get; set; } = "";
        public static string Token { get; set; } = "";

        public static void Clear()
        {
            EmployeeId = "";
            Name = "";
            Role = "";
            Token = "";
        }
    }
}
