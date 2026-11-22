using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ArInventory.Local;

// Обрабатывает нажатия кнопок QR-панели, размещённой в SampleScene.
public class QRNavigationController : MonoBehaviour
{
    [SerializeField] private Button rescanButton;

    private QRScanner scanner;

    private void OnEnable()
    {
        BindScannerEvents(QRScanner.Instance);
    }

    private void OnDisable()
    {
        BindScannerEvents(null);
    }

    // Назначается на кнопку «Сканировать другой QR-код» в Inspector.
    public void StartScanning()
    {
        BindScannerEvents(QRScanner.Instance);

        if (SessionManager.IsLoggedIn && LocalDatabase.Instance != null && LocalDatabase.Instance.IsReady)
        {
            LocalInventoryBatch activeBatch = LocalDatabase.Instance.GetActiveInventoryBatch(SessionManager.UserId);
            if (activeBatch == null)
            {
                LocalInventoryBatch latestBatch = LocalDatabase.Instance.GetLatestInventoryBatch(SessionManager.UserId);
                if (latestBatch != null && latestBatch.status == "completed")
                {
                    SceneManager.LoadScene("StartScene");
                    return;
                }
            }
            else if (LocalDatabase.Instance.GetCompletedBoxCount(activeBatch.client_batch_id) >= activeBatch.expected_box_count)
            {
                SceneManager.LoadScene("StartScene");
                return;
            }
        }

        if (scanner == null)
        {
            Debug.LogWarning("[QR] Сканер недоступен.");
            return;
        }

        scanner.StartScanning();
        if (rescanButton != null)
            rescanButton.interactable = !scanner.IsScanning;
    }

    // Назначается на кнопку «Главное меню» в Inspector.
    public void ReturnToMainMenu()
    {
        QRScanner.Instance?.CancelScanning();
        SceneManager.LoadScene("StartScene");
    }

    /// <summary>Закрывает отправленный отчёт и запускает сканер следующей назначенной коробки.</summary>
    public void ContinueAfterReport(GameObject reportScreen)
    {
        StartCoroutine(CloseReportAndScan(reportScreen));
    }

    private IEnumerator CloseReportAndScan(GameObject reportScreen)
    {
        if (reportScreen != null)
            Destroy(reportScreen);

        // Даём Unity убрать Canvas отчёта до открытия системного окна камеры.
        yield return null;
        StartScanning();
    }

    private void BindScannerEvents(QRScanner newScanner)
    {
        if (scanner == newScanner) return;

        if (scanner != null)
        {
            scanner.OnScanCancelled -= EnableRescanButton;
            scanner.OnScanRejected -= ShowScanRejection;
            scanner.OnQRScanned -= OnQrScanned;
        }

        scanner = newScanner;
        if (scanner != null)
        {
            scanner.OnScanCancelled += EnableRescanButton;
            scanner.OnScanRejected += ShowScanRejection;
            scanner.OnQRScanned += OnQrScanned;
        }
    }

    /// <summary>Показывает причину отказа в существующем уведомлении AR-сцены.</summary>
    private void ShowScanRejection(string message)
    {
        VoiceHelpController.Instance?.ShowFeedback(message);
    }

    private void EnableRescanButton()
    {
        if (rescanButton != null)
            rescanButton.interactable = true;
    }

    private void OnQrScanned(string _)
    {
        EnableRescanButton();
    }
}
