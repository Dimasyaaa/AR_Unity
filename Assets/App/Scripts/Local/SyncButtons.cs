using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ArInventory.Local;
using ArInventory.Network;

/// <summary>Показывает выбор канала синхронизации и запускает отправку по Wi-Fi или USB.</summary>
public class SyncButtons : MonoBehaviour
{
    [Header("Главная кнопка")]
    [SerializeField] private Button syncButton;   // "Синхронизировать"

    [Header("Кнопки каналов (появляются после нажатия)")]
    [SerializeField] private Button wifiButton;   // "Wi-Fi"
    [SerializeField] private Button usbButton;    // "USB"

    [Header("Статус")]
    [SerializeField] private TMP_Text statusText;

    /// <summary>Подключает кнопки и скрывает выбор канала до нажатия на «Синхронизировать».</summary>
    private void Start()
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

    /// <summary>Показывает или скрывает кнопки выбора канала.</summary>
    private void ToggleChannels()
    {
        bool show = (wifiButton != null) && !wifiButton.gameObject.activeSelf;
        if (wifiButton != null) wifiButton.gameObject.SetActive(show);
        if (usbButton != null) usbButton.gameObject.SetActive(show);
        if (!show) SetStatus("");
    }

    /// <summary>Сохраняет выбранный адрес API и запускает отправку локальных записей.</summary>
    private void DoSync(string url)
    {
        InventoryApiClient.RememberBaseUrl(url);
        SetStatus("Отправка...");
        SyncManager.GetOrCreate().SyncNow(url, msg =>
        {
            SetStatus(msg);
            Debug.Log("[Sync] " + msg);
        });
    }

    /// <summary>Показывает итог синхронизации под кнопками.</summary>
    private void SetStatus(string s)
    {
        if (statusText != null)
            statusText.text = s;
    }
}
