/// <summary>
/// Хранит ID и имя вошедшего пользователя до завершения приложения.
/// Статический класс сохраняет их при переходах между сценами, но не записывает на диск.
/// </summary>
public static class SessionManager
{
    // Переключатель режимов: false — общая опись без входа, true — прежний вход сотрудника.
    // Для возврата авторизации установите true и включите LoginScene в Build Settings.
    public const bool AuthenticationEnabled = false;

    // Ноль — общий терминал; отрицательное значение означает, что вход в старом режиме не выполнен.
    public static int UserId { get; private set; } = AuthenticationEnabled ? -1 : 0;

    /// <summary>Имя сотрудника или подпись общего режима в главном меню.</summary>
    public static string FullName { get; private set; } = AuthenticationEnabled ? "" : "Общий терминал";

    /// <summary>В общем режиме вход не требуется; старое правило оставлено для обратного переключения.</summary>
    public static bool IsLoggedIn => !AuthenticationEnabled || UserId > 0;
    // Старая проверка для режима с авторизацией: public static bool IsLoggedIn => UserId > 0;

    /// <summary>Сохраняет данные пользователя после успешной проверки логина локальной базой.</summary>
    public static void SetUser(int userId, string fullName)
    {
        UserId = userId;
        FullName = fullName;
    }

}
