using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Управляет логикой главного меню, обработкой нажатий кнопок и переходом в AR-сцену
public class MainMenuManager : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private GameObject instructionsPanel;

    [Header("Buttons")]
    [SerializeField] private Button scanButton;
    [SerializeField] private Button instructionsButton;
    [SerializeField] private Button exitButton;
    [SerializeField] private Button closeButton;

    void Start()
    {
        // Привязываем методы к событиям нажатия кнопок
        scanButton.onClick.AddListener(OnScanClicked);
        instructionsButton.onClick.AddListener(OnInstructionsClicked);
        exitButton.onClick.AddListener(OnExitClicked);
        closeButton.onClick.AddListener(OnCloseClicked);

        // Скрываем панель инструкций при запуске
        if (instructionsPanel != null)
            instructionsPanel.SetActive(false);
    }

    private void OnScanClicked()
    {
        Debug.Log("[Menu] Starting QR scan...");

        if (QRScanner.Instance != null)
        {
            // Подписываемся на событие сканирования и запускаем процесс
            QRScanner.Instance.OnQRScanned += OnQRScanned;
            QRScanner.Instance.StartScanning();
        }
    }

    // Вызывается при успешном сканировании QR-кода
    private void OnQRScanned(string qrData)
    {
        Debug.Log($"[Menu] QR scanned: {qrData}");

        // Сохраняем данные для использования в следующей сцене
        GameDataManager.LastScannedQR = qrData;

        // Отписываемся от события, чтобы избежать дублирования вызовов
        if (QRScanner.Instance != null)
            QRScanner.Instance.OnQRScanned -= OnQRScanned;

        // Загружаем основную AR-сцену
        SceneManager.LoadScene("SampleScene");
    }

    private void OnInstructionsClicked()
    {
        Debug.Log("[Menu] Showing instructions");
        if (instructionsPanel != null)
            instructionsPanel.SetActive(true);
    }

    private void OnCloseClicked()
    {
        Debug.Log("[Menu] Closing instructions");
        if (instructionsPanel != null)
            instructionsPanel.SetActive(false);
    }

    private void OnExitClicked()
    {
        Debug.Log("[Menu] Exiting application");

#if UNITY_EDITOR
        // Остановка режима Play в редакторе
        UnityEditor.EditorApplication.isPlaying = false;
#else
        // Закрытие приложения на мобильном устройстве
        Application.Quit();
#endif
    }
}