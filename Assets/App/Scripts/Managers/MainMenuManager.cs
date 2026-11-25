using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;
using ArInventory.Local;
using ArInventory.Network;
using System.Collections;
using System.Collections.Generic;
using System.Text;

/// <summary>Показывает данные вошедшего пользователя и обрабатывает кнопки главного меню.</summary>
public class MainMenuManager : MonoBehaviour
{
    private const string StartAfterSelectionKey = "ArInventory.StartAfterSelection";

    [Header("Элементы меню")]
    [SerializeField] private GameObject instructionsPanel;
    [SerializeField] private TMP_Text userInfoText;
    [SerializeField] private TMP_Text batchProgressText;
    [SerializeField] private GameObject batchSummaryPanel;
    [SerializeField] private TMP_Text batchSummaryText;

    private bool assignmentRequestInProgress;

    /// <summary>Обновляет подпись пользователя при открытии меню.</summary>
    private void Start()
    {
        // При запуске без LoginScene создаём базу прямо из главного меню.
        LocalDatabase.GetOrCreate();
        LocalClient.GetOrCreate();

        if (userInfoText != null)
        {
            userInfoText.text = SessionManager.AuthenticationEnabled
                ? SessionManager.IsLoggedIn
                    ? $"Пользователь: {SessionManager.FullName}"
                    : "Пользователь: не авторизован"
                : "Общий режим — без авторизации";
        }

        if (batchSummaryPanel != null)
            batchSummaryPanel.SetActive(false);
        RefreshBatchProgress(showCompletedSummary: true);

        // После подтверждения назначения возвращаем пользователя к главному меню и сразу открываем сканер.
        if (PlayerPrefs.GetInt(StartAfterSelectionKey, 0) == 1)
        {
            PlayerPrefs.DeleteKey(StartAfterSelectionKey);
            StartCoroutine(StartScanWhenLocalDataIsReady());
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
        if (assignmentRequestInProgress)
            return;
        if (!SessionManager.IsLoggedIn)
        {
            SetBatchMessage("Сначала войдите в систему.");
            return;
        }

        LocalDatabase database = LocalDatabase.Instance;
        if (database == null || !database.IsReady)
        {
            SetBatchMessage("Локальная база ещё загружается. Попробуйте через секунду.");
            return;
        }

        assignmentRequestInProgress = true;
        SetBatchMessage("Проверяем назначение инвентаризации…");
        InventoryApiClient.GetActiveAssignment(this, SessionManager.UserId,
            assignment =>
            {
                assignmentRequestInProgress = false;
                var batch = database.SaveAssignedInventoryBatch(
                    assignment.clientBatchId,
                    SessionManager.UserId,
                    System.Array.ConvertAll(assignment.boxes, box => box.qrCode));
                if (batch == null)
                {
                    SetBatchMessage("Не удалось сохранить назначение. Проверьте локальную базу и повторите.");
                    return;
                }

                if (database.GetCompletedBoxCount(batch.client_batch_id) >= batch.expected_box_count)
                {
                    batch.status = "completed";
                    database.UpdateInventoryBatch(batch);
                    ShowBatchSummary();
                    RefreshBatchProgress(showCompletedSummary: true);
                    return;
                }
                BeginQrScanner();
            },
            error =>
            {
                assignmentRequestInProgress = false;
                LocalInventoryBatch cached = database.GetActiveInventoryBatch(SessionManager.UserId);
                if (cached != null && !string.IsNullOrWhiteSpace(cached.assigned_qr_codes))
                {
                    Debug.LogWarning("[Menu] API недоступен, продолжаем по сохранённому назначению: " + error);
                    BeginQrScanner();
                    return;
                }

                LocalInventoryBatch latest = database.GetLatestInventoryBatch(SessionManager.UserId);
                if (latest != null && latest.status == "completed")
                {
                    ShowBatchSummary();
                    RefreshBatchProgress(showCompletedSummary: true);
                    return;
                }

                SetBatchMessage(SessionManager.AuthenticationEnabled
                    ? "Нет активного назначения от администратора. Подключитесь к серверу и попросите назначить коробки."
                    : "Активная опись не найдена. Загрузите и запустите её в панели администратора.");
                Debug.LogWarning("[Menu] Не удалось получить назначение: " + error);
            });
    }

    /// <summary>Открывает отдельный экран активного назначения и истории сотрудника.</summary>
    public void OpenInventorySelection()
    {
        // В общем режиме не показываем экран сотрудника, а сразу открываем сканер описи.
        if (!SessionManager.AuthenticationEnabled)
        {
            StartQrScan();
            return;
        }

        if (!SessionManager.IsLoggedIn)
        {
            SetBatchMessage("Сначала войдите в систему.");
            return;
        }

        SceneManager.LoadScene("InventorySelectionScene");
    }

    /// <summary>Возвращает приложение в меню и после загрузки продолжает назначенное сканирование.</summary>
    public static void ResumeAssignedInventoryScan()
    {
        PlayerPrefs.SetInt(StartAfterSelectionKey, 1);
        SceneManager.LoadScene("StartScene");
    }

    private IEnumerator StartScanWhenLocalDataIsReady()
    {
        float deadline = Time.realtimeSinceStartup + 10f;
        while (Time.realtimeSinceStartup < deadline)
        {
            LocalDatabase database = LocalDatabase.Instance;
            if (database != null && database.IsReady)
            {
                StartQrScan();
                yield break;
            }
            yield return null;
        }

        SetBatchMessage("Локальная база не успела загрузиться. Нажмите «Начать инвентаризацию» ещё раз.");
    }

    /// <summary>Открывает сканер только после получения или восстановления назначения.</summary>
    private void BeginQrScanner()
    {
        if (batchSummaryPanel != null)
            batchSummaryPanel.SetActive(false);
        RefreshBatchProgress();

        QRScanner scanner = QRScanner.Instance;
        if (scanner == null)
        {
            Debug.LogWarning("[Menu] QR scanner is not available");
            SetBatchMessage("Сканер QR-кодов недоступен в этой сцене.");
            return;
        }

#if UNITY_EDITOR
        // При повторном запуске теста сразу пропускаем уже проверенные демонстрационные коробки.
        scanner.SelectNextUncompletedEditorQr(SessionManager.UserId);
#endif

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

    /// <summary>Показывает итоговую таблицу последнего завершённого запуска.</summary>
    public void ShowBatchSummary()
    {
        LocalDatabase database = LocalDatabase.Instance;
        LocalInventoryBatch batch = database != null && database.IsReady
            ? database.GetLatestInventoryBatch(SessionManager.UserId)
            : null;
        if (batch == null)
        {
            SetBatchMessage("Пока нет сохранённой общей инвентаризации.");
            return;
        }

        if (batchSummaryText != null)
        {
            batchSummaryText.text = BuildBatchSummary(batch);
            ResizeBatchSummary();
        }
        if (batchSummaryPanel != null)
            batchSummaryPanel.SetActive(true);
    }

    /// <summary>Закрывает итоговую таблицу, оставляя общий прогресс доступным в меню.</summary>
    public void CloseBatchSummary()
    {
        if (batchSummaryPanel != null)
            batchSummaryPanel.SetActive(false);
        RefreshBatchProgress();
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

        LocalDatabase database = LocalDatabase.Instance;
        LocalInventoryBatch batch = database != null && database.IsReady
            ? database.GetActiveInventoryBatch(SessionManager.UserId)
            : null;
        if (batch == null)
        {
            ShowQrRejection("Нет активной описи. Вернитесь в меню и проверьте её в панели администратора.");
            return;
        }

        if (!database.IsQrAssignedToBatch(batch.client_batch_id, qrData))
        {
            ShowQrRejection($"QR «{qrData}» не входит в активную опись. Отсканируйте QR-код из неё.");
            return;
        }

        if (database.HasCompletedBox(batch.client_batch_id, qrData))
        {
            ShowQrRejection($"Коробка «{qrData}» уже проверена. Отсканируйте следующую назначенную коробку.");
            return;
        }

        int completed = database.GetCompletedBoxCount(batch.client_batch_id);
        if (completed >= batch.expected_box_count)
        {
            SetBatchMessage("Все коробки проверены. Откройте итоговую таблицу.");
            StopListeningForQr();
            ShowBatchSummary();
            return;
        }

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

    /// <summary>Показывает понятное сообщение, когда сотрудник сканирует неподходящую коробку.</summary>
    private void ShowQrRejection(string message)
    {
        SetBatchMessage(message);
        VoiceHelpController.Instance?.ShowFeedback(message);
        StopListeningForQr();
    }

    private void RefreshBatchProgress(bool showCompletedSummary = false)
    {
        if (!SessionManager.IsLoggedIn)
            return;

        LocalDatabase database = LocalDatabase.Instance;
        if (database == null || !database.IsReady)
            return;

        LocalInventoryBatch active = database.GetActiveInventoryBatch(SessionManager.UserId);
        if (active != null)
        {
            int completed = database.GetCompletedBoxCount(active.client_batch_id);
            int remaining = Mathf.Max(0, active.expected_box_count - completed);
            SetBatchMessage($"Активная инвентаризация: проверено {completed} / {active.expected_box_count} · осталось {remaining}");
            return;
        }

        LocalInventoryBatch latest = database.GetLatestInventoryBatch(SessionManager.UserId);
        if (latest != null && latest.status == "completed")
        {
            int completed = database.GetCompletedBoxCount(latest.client_batch_id);
            SetBatchMessage($"Общая инвентаризация завершена: {completed} / {latest.expected_box_count} коробок.");
            if (showCompletedSummary && batchSummaryPanel != null)
                ShowBatchSummary();
        }
    }

    private void SetBatchMessage(string text)
    {
        if (batchProgressText != null)
        {
            batchProgressText.text = text;
            return;
        }

        if (userInfoText != null)
            userInfoText.text = SessionManager.IsLoggedIn
                ? SessionManager.AuthenticationEnabled
                    ? $"Пользователь: {SessionManager.FullName}\n{text}"
                    : $"Общий терминал\n{text}"
                : text;
    }

    private string BuildBatchSummary(LocalInventoryBatch batch)
    {
        List<LocalInventoryBatchSummaryRow> rows = LocalDatabase.Instance
            .GetInventoryBatchSummary(batch.client_batch_id);
        var summary = new StringBuilder();
        int completedBoxes = LocalDatabase.Instance.GetCompletedBoxCount(batch.client_batch_id);
        summary.AppendLine("<size=34><b>Итоги инвентаризации</b></size>");
        summary.AppendLine($"<size=22><color=#34C88A>Проверено коробок: {completedBoxes} из {batch.expected_box_count}</color></size>");
        summary.AppendLine("<size=20><color=#8FA5C3>Позиции сгруппированы по коробкам. Учётное и фактическое количество указаны отдельно.</color></size>\n");

        int boxNumber = 0;
        string previousQr = null;
        foreach (LocalInventoryBatchSummaryRow row in rows)
        {
            if (!string.Equals(previousQr, row.qrCode, System.StringComparison.Ordinal))
            {
                if (boxNumber > 0) summary.AppendLine();
                boxNumber++;
                previousQr = row.qrCode;
                summary.AppendLine($"<size=25><b>{boxNumber:00}. {EscapeRichText(row.boxTitle)}</b></size>");
                summary.AppendLine($"<size=18><color=#72B7F2>QR: {EscapeRichText(row.qrCode)}</color></size>");
            }

            string status = row.actualQuantity <= 0
                ? "Нет"
                : row.actualQuantity < row.expectedQuantity ? "Частично" : "Есть";
            string statusColor = row.actualQuantity <= 0 ? "#FF7777" : row.actualQuantity < row.expectedQuantity ? "#F4C65E" : "#45D79A";
            summary.AppendLine($"<size=19>• {EscapeRichText(row.itemName)}  <color=#AAB8CB>Учёт: {row.expectedQuantity} · Факт: {row.actualQuantity}</color>  <color={statusColor}><b>{status}</b></color></size>");
        }

        return summary.ToString();
    }

    private void ResizeBatchSummary()
    {
        // Высота содержимого подстраивается под число пунктов, поэтому прокрутка не уходит в пустое место.
        if (batchSummaryText == null) return;
        ScrollRect scroll = batchSummaryText.GetComponentInParent<ScrollRect>();
        if (scroll == null || scroll.content == null || scroll.viewport == null) return;

        Canvas.ForceUpdateCanvases();
        float preferredHeight = batchSummaryText.GetPreferredValues(
            batchSummaryText.text, batchSummaryText.rectTransform.rect.width, 0f).y;
        float contentHeight = Mathf.Max(scroll.viewport.rect.height, preferredHeight + 48f);
        batchSummaryText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentHeight);
        scroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentHeight);
        Canvas.ForceUpdateCanvases();
        scroll.verticalNormalizedPosition = 1f;
    }

    private static string EscapeRichText(string value)
    {
        return (value ?? string.Empty)
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
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
