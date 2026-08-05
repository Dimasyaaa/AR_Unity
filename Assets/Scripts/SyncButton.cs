using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ArInventory.Local;

// Одна кнопка "Синхронизировать"; по нажатию раскрываются Wi-Fi и USB.
public class SyncButtons : MonoBehaviour
{
    [Header("Главная кнопка")]
    [SerializeField] private Button syncButton;   // "Синхронизировать"

    [Header("Кнопки каналов (появляются после нажатия)")]
    [SerializeField] private Button wifiButton;   // "Wi-Fi"
    [SerializeField] private Button usbButton;    // "USB"

    [Header("Статус")]
    [SerializeField] private TMP_Text statusText;

    void Start()
    {
        if (syncButton != null)
            syncButton.onClick.AddListener(ToggleChannels);

        if (wifiButton != null)
        {
            wifiButton.onClick.AddListener(() => DoSync(SyncManager.WIFI_URL));
            wifiButton.gameObject.SetActive(false); // скрыта до нажатия
        }

        if (usbButton != null)
        {
            usbButton.onClick.AddListener(() => DoSync(SyncManager.USB_URL));
            usbButton.gameObject.SetActive(false); // скрыта до нажатия
        }

        SetStatus("");
    }

    // Показать / скрыть кнопки Wi-Fi и USB
    private void ToggleChannels()
    {
        bool show = (wifiButton != null) && !wifiButton.gameObject.activeSelf;
        if (wifiButton != null) wifiButton.gameObject.SetActive(show);
        if (usbButton != null) usbButton.gameObject.SetActive(show);
        if (!show) SetStatus("");
    }

    public void DoSync(string url)
    {
        SetStatus("Отправка...");
        SyncManager.GetOrCreate().SyncNow(url, msg =>
        {
            SetStatus(msg);
            Debug.Log("[Sync] " + msg);
        });
    }

    private void SetStatus(string s)
    {
        if (statusText != null)
            statusText.text = s;
    }
}