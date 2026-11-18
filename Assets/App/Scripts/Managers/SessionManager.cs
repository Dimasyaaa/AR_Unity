/// <summary>
/// Хранит ID и имя вошедшего пользователя до завершения приложения.
/// Статический класс сохраняет их при переходах между сценами, но не записывает на диск.
/// </summary>
public static class SessionManager
{
    // Значение -1 означает, что успешного входа в текущем запуске приложения не было.
    public static int UserId { get; private set; } = -1;

    /// <summary>Имя вошедшего пользователя; используется в главном меню и журнале.</summary>
    public static string FullName { get; private set; } = "";

    // Положительный ID означает, что пользователь успешно вошёл.
    public static bool IsLoggedIn => UserId > 0;

    /// <summary>Сохраняет данные пользователя после успешной проверки логина локальной базой.</summary>
    public static void SetUser(int userId, string fullName)
    {
        UserId = userId;
        FullName = fullName;
    }

}
