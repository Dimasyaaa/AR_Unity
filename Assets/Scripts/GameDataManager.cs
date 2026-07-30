// Статический класс для хранения данных (результата сканирования) 
// при переходе между сценами без использования DontDestroyOnLoad
public static class GameDataManager
{
    public static string LastScannedQR { get; set; }
}