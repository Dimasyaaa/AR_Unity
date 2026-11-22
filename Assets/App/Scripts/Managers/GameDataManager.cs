/// <summary>
/// Передаёт данные, нужные следующей сцене, во время перехода между сценами.
/// Сейчас здесь хранится текст последнего отсканированного QR-кода.
/// </summary>
public static class GameDataManager
{
    // MainMenuManager записывает QR перед открытием SampleScene,
    // а AR-карточки читают его при загрузке данных коробки.
    public static string LastScannedQR { get; set; }
}
