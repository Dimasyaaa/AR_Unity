//  данные текущего пользователя между сценами
public static class SessionManager
{
    public static int UserId { get; private set; } = -1;
    public static string FullName { get; private set; } = "";
    public static string Department { get; private set; } = "";

    public static bool IsLoggedIn => UserId > 0;

    public static void SetUser(int userId, string fullName, string department)
    {
        UserId = userId;
        FullName = fullName;
        Department = department;
    }

    public static void Logout()
    {
        UserId = -1;
        FullName = "";
        Department = "";
    }
}