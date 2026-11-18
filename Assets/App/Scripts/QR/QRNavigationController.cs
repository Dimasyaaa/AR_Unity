using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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

    private void BindScannerEvents(QRScanner newScanner)
    {
        if (scanner == newScanner) return;

        if (scanner != null)
        {
            scanner.OnScanCancelled -= EnableRescanButton;
            scanner.OnQRScanned -= OnQrScanned;
        }

        scanner = newScanner;
        if (scanner != null)
        {
            scanner.OnScanCancelled += EnableRescanButton;
            scanner.OnQRScanned += OnQrScanned;
        }
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
