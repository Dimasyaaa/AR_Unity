using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using ArInventory.Local;

/// <summary>Показывает данные вошедшего пользователя и обрабатывает кнопки главного меню.</summary>
public class MainMenuManager : MonoBehaviour
{
    [Header("Элементы меню")]
    [SerializeField] private GameObject instructionsPanel;
    [SerializeField] private TMP_Text userInfoText;

    /// <summary>Обновляет подпись пользователя при открытии меню.</summary>
    private void Start()
    {
        // Гарантируем наличие локального клиента, если меню запустили отдельно в Editor.
        LocalClient.GetOrCreate();

        if (userInfoText != null)
        {
            userInfoText.text = SessionManager.IsLoggedIn
                ? $"Пользователь: {SessionManager.FullName}"
                : "Пользователь: не авторизован";
        }
    }

    /// <summary>Снимает подписки на события сканера при выгрузке меню.</summary>
    private void OnDestroy()
    {
        // Снимаем подписки, чтобы старый объект меню не реагировал на следующее сканирование.
        StopListeningForQr();
    }

    /// <summary>Подписывается на результат сканирования и запускает QR-сканер.</summary>
    public void StartQrScan()
    {
        Debug.Log("[Menu] Starting QR scan...");

        QRScanner scanner = QRScanner.Instance;
        if (scanner == null)
        {
            Debug.LogWarning("[Menu] QR scanner is not available");
            return;
        }

        // Сначала снимаем старую подписку: повторное нажатие не задвоит обработчик.
        StopListeningForQr();
        scanner.OnQRScanned += OnQRScanned;
        scanner.OnScanCancelled += OnScanCancelled;
        scanner.StartScanning();
    }

    /// <summary>Показывает панель инструкции.</summary>
    public void ShowInstructions()
    {
        if (instructionsPanel != null)
            instructionsPanel.SetActive(true);
    }

    /// <summary>Закрывает панель инструкции.</summary>
    public void CloseInstructions()
    {
        if (instructionsPanel != null)
            instructionsPanel.SetActive(false);
    }

    /// <summary>Останавливает Play Mode в редакторе или закрывает приложение на устройстве.</summary>
    public void ExitApplication()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>Сохраняет отсканированный QR, записывает событие и открывает AR-сцену.</summary>
    private void OnQRScanned(string qrData)
    {
        Debug.Log($"[Menu] QR scanned: {qrData}");
        GameDataManager.LastScannedQR = qrData;
        StopListeningForQr();

        if (LocalClient.Instance != null && SessionManager.IsLoggedIn)
        {
            LocalClient.Instance.SendScan(
                qrData,
                onSuccess: response =>
                    Debug.Log($"[Menu] QR сохранён в журнале: {response.objectName}; повторное сканирование: {response.wasRepeated}"),
                onError: err =>
                    Debug.LogWarning($"[Menu] Scan not saved: {err}"));
        }
        else
        {
            Debug.LogWarning("[Menu] Scan not saved: пользователь не вошел или LocalClient недоступен");
        }

        SceneManager.LoadScene("SampleScene");
    }

    /// <summary>Снимает ожидание результата, если пользователь закрыл сканер.</summary>
    private void OnScanCancelled()
    {
        // После отмены сканирования меню больше не ждёт результат.
        StopListeningForQr();
    }

    /// <summary>Удаляет обработчики с текущего QR-сканера, чтобы не накапливать подписки.</summary>
    private void StopListeningForQr()
    {
        // Операция безопасна при повторном вызове: удалённую подписку снять можно ещё раз.
        QRScanner scanner = QRScanner.Instance;
        if (scanner == null)
            return;

        scanner.OnQRScanned -= OnQRScanned;
        scanner.OnScanCancelled -= OnScanCancelled;
    }
}
