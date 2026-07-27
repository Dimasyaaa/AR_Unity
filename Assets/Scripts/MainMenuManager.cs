using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
        // Настраиваем кнопки
        scanButton.onClick.AddListener(OnScanClicked);
        instructionsButton.onClick.AddListener(OnInstructionsClicked);
        exitButton.onClick.AddListener(OnExitClicked);
        closeButton.onClick.AddListener(OnCloseClicked);

        // Скрываем панель инструкции
        if (instructionsPanel != null)
            instructionsPanel.SetActive(false);
    }

    private void OnScanClicked()
    {
        Debug.Log("[Menu] Starting QR scan...");

        if (QRScanner.Instance != null)
        {
            QRScanner.Instance.OnQRScanned += OnQRScanned;
            QRScanner.Instance.StartScanning();
        }
    }

    private void OnQRScanned(string qrData)
    {
        Debug.Log($"[Menu] QR scanned: {qrData}");
        GameDataManager.LastScannedQR = qrData;

        if (QRScanner.Instance != null)
            QRScanner.Instance.OnQRScanned -= OnQRScanned;

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
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}