using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ArInventory.Local
{
    /// <summary>Отправляет локальный журнал и завершённые инвентаризации на сервер.</summary>
    public class SyncManager : MonoBehaviour
    {
        /// <summary>Единственный активный компонент синхронизации.</summary>
        public static SyncManager Instance { get; private set; }

        /// <summary>Адрес USB-туннеля после выполнения adb reverse на компьютере.</summary>
        public const string USB_URL = "http://127.0.0.1:8000";

        /// <summary>Адрес API компьютера в локальной Wi-Fi-сети.</summary>
        public const string WIFI_URL = "http://192.168.0.51:8000";

        /// <summary>Оставляет один экземпляр компонента между переходами сцен.</summary>
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>Возвращает текущий компонент или создаёт его при необходимости.</summary>
        public static SyncManager GetOrCreate()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("SyncManager");
            go.AddComponent<SyncManager>();
            return Instance;
        }

        /// <summary>Начинает фоновую синхронизацию по выбранному адресу API.</summary>
        public void SyncNow(string baseUrl, Action<string> onDone)
        {
            StartCoroutine(SyncRoutine(baseUrl, onDone));
        }

        /// <summary>Отправляет обычные события журнала, затем по одному передаёт сохранённые проверки.</summary>
        private IEnumerator SyncRoutine(string baseUrl, Action<string> onDone)
        {
            if (LocalDatabase.Instance == null || !LocalDatabase.Instance.IsReady)
            { onDone?.Invoke("База не готова"); yield break; }

            baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? InventoryApiDefaultUrl() : baseUrl.Trim().TrimEnd('/');
            if (Application.platform == RuntimePlatform.Android &&
                Uri.TryCreate(baseUrl, UriKind.Absolute, out var selectedUri) && selectedUri.IsLoopback)
            {
                Debug.LogWarning($"[Sync] USB-адрес {baseUrl} указывает на телефон. Для синхронизации использую Wi-Fi: {WIFI_URL}");
                baseUrl = WIFI_URL;
            }
            Debug.Log($"[Sync] Синхронизация через {baseUrl}");

            // Сначала отправляем обычные события: вход, сканирование QR и расчёты.
            var unsynced = LocalDatabase.Instance.Connection
                .Table<LocalInventorySession>()
                .Where(s => !s.synced)
                .ToList();

            string sessionSummary = "Нет новых записей";
            if (unsynced.Count > 0)
            {
                var payload = new SyncPayload();
                payload.items = unsynced.Select(s =>
                {
                    var u = LocalDatabase.Instance.Connection
                        .Table<LocalUser>()
                        .FirstOrDefault(x => x.id == s.user_id);

                    return new SyncDto
                    {
                        userId = s.user_id,
                        qrCodeId = s.qr_code_id ?? 0,
                        action = s.action,
                        comment = s.comment ?? "",
                        actionTime = s.action_time.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                        fullName = u != null ? u.full_name : "",
                        department = u != null ? u.department : ""
                    };
                }).ToList();

                string json = JsonUtility.ToJson(payload);
                using (var req = new UnityWebRequest(baseUrl + "/api/sync/sessions", "POST"))
                {
                    req.SetRequestHeader("Content-Type", "application/json");
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                    req.timeout = 5;

                    yield return req.SendWebRequest();

                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        foreach (var s in unsynced)
                        {
                            s.synced = true;
                            LocalDatabase.Instance.Connection.Update(s);
                        }
                        var res = JsonUtility.FromJson<SyncResult>(req.downloadHandler.text);
                        sessionSummary = res != null
                            ? $"Журнал: импортировано {res.imported}, пропущено {res.skipped}"
                            : "Журнал: сервер ответил, но вернул некорректный результат";
                    }
                    else
                    {
                        onDone?.Invoke($"Ошибка синхронизации журнала: {FormatRequestError(req, baseUrl)}");
                        yield break;
                    }
                }
            }

            // Отдельно отправляем отчёты с фотографиями; голосовой комментарий необязателен.
            var pendingRuns = LocalDatabase.Instance.Connection.Query<LocalInventoryRun>(
                "SELECT id FROM local_inventory_runs WHERE synced = 0 ORDER BY id");
            if (pendingRuns.Count == 0) { onDone?.Invoke(sessionSummary); yield break; }

            int importedRuns = 0;
            int duplicateRuns = 0;
            int rejectedRuns = 0;
            string firstRejection = null;

            // Отправляем проверки по одной: устаревшая запись не должна задерживать остальные.
            foreach (var pending in pendingRuns)
            {
                var run = LocalDatabase.Instance.Connection.Find<LocalInventoryRun>(pending.id);
                if (run == null || run.synced) continue;
                if (!TryBuildRunDto(run, out var runDto, out var validationError))
                {
                    rejectedRuns++;
                    if (string.IsNullOrEmpty(firstRejection)) firstRejection = validationError;
                    continue;
                }

                var runPayload = new InventoryRunsSyncPayload
                {
                    items = new List<InventoryRunSyncDto> { runDto }
                };

                using (var req = new UnityWebRequest(baseUrl + "/api/sync/inventory-runs", "POST"))
                {
                    req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                    req.downloadHandler = new DownloadHandlerBuffer();
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(runPayload)));
                    req.timeout = 45;
                    yield return req.SendWebRequest();
                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        onDone?.Invoke($"{sessionSummary}. Инвентаризация: {FormatRequestError(req, baseUrl)}");
                        yield break;
                    }

                    var result = JsonUtility.FromJson<InventoryRunsSyncResult>(req.downloadHandler.text);
                    if (result == null || result.skipped > 0 || result.imported + result.duplicates != 1)
                    {
                        rejectedRuns++;
                        string detail = result != null && result.errors != null && result.errors.Count > 0
                            ? result.errors[0]
                            : "Сервер не подтвердил сохранение проверки.";
                        if (string.IsNullOrEmpty(firstRejection)) firstRejection = detail;
                        continue;
                    }

                    LocalDatabase.Instance.MarkInventoryRunSynced(run.client_run_id);
                    importedRuns += result.imported;
                    duplicateRuns += result.duplicates;
                }
            }

            string inventorySummary = $"Инвентаризация: отправлено {importedRuns}, уже было {duplicateRuns}";
            if (rejectedRuns > 0)
                inventorySummary += $", оставлено на устройстве {rejectedRuns}. Причина: {firstRejection}";
            onDone?.Invoke($"{sessionSummary}. {inventorySummary}");
        }

        /// <summary>Проверяет старый локальный отчёт и собирает DTO, понятный текущему API.</summary>
        private static bool TryBuildRunDto(LocalInventoryRun run, out InventoryRunSyncDto dto, out string error)
        {
            dto = null;
            error = null;
            var database = LocalDatabase.Instance;
            if (database == null || !database.IsReady)
            {
                error = "Локальная база не готова.";
                return false;
            }

            if (!Guid.TryParse(run.client_run_id, out _))
            {
                error = "У локальной проверки повреждён идентификатор.";
                return false;
            }

            if (!string.IsNullOrEmpty(run.report_payload))
            {
                try
                {
                    dto = JsonUtility.FromJson<InventoryRunSyncDto>(run.report_payload);
                    bool hasPhotos = dto != null &&
                        (dto.photosBase64 != null && dto.photosBase64.Length > 0 ||
                         !string.IsNullOrEmpty(dto.photoBase64));
                    if (dto != null && dto.clientRunId == run.client_run_id &&
                        hasPhotos)
                        return true;
                }
                catch (ArgumentException) { }
                error = "Не удалось прочитать сохранённый отчёт с вложениями.";
                return false;
            }

            if (database.Connection.Table<LocalUser>().FirstOrDefault(user => user.id == run.user_id) == null)
            {
                error = "Сотрудник старой проверки отсутствует в актуальной локальной базе.";
                return false;
            }

            var cabinet = database.FindCabinetByQr(run.qr_code);
            if (cabinet == null)
            {
                error = "Старая проверка сохранена для QR-кода, который больше не привязан к шкафу. Она оставлена на устройстве.";
                return false;
            }

            var checklist = database.GetCabinetChecklist(cabinet.id);
            var savedItems = database.Connection.Table<LocalInventoryRunItem>()
                .Where(item => item.run_id == run.id).ToList();
            if (savedItems.Count == 0 || savedItems.Count != checklist.Count)
            {
                error = "Список предметов старой проверки отличается от текущего чек-листа. Она оставлена на устройстве.";
                return false;
            }

            var syncItems = new List<InventoryRunItemSyncDto>(savedItems.Count);
            var mappedIds = new HashSet<long>();
            bool referencesChanged = run.cabinet_id != cabinet.id || run.qr_code_id != cabinet.qr_code_id;
            foreach (var savedItem in savedItems)
            {
                var currentItem = checklist.FirstOrDefault(item => item.id == savedItem.checklist_item_id);
                if (currentItem == null && !string.IsNullOrWhiteSpace(savedItem.item_name_snapshot))
                    currentItem = checklist.FirstOrDefault(item =>
                        !string.IsNullOrWhiteSpace(item.item_name) && string.Equals(
                            item.item_name.Trim(), savedItem.item_name_snapshot.Trim(), StringComparison.OrdinalIgnoreCase));

                if (currentItem == null || !mappedIds.Add(currentItem.id))
                {
                    error = "Не удалось сопоставить предмет старой проверки с текущим чек-листом. Она оставлена на устройстве.";
                    return false;
                }
                if (savedItem.status != "present" && savedItem.status != "missing")
                {
                    error = "В старой проверке есть предмет без отметки. Она оставлена на устройстве.";
                    return false;
                }

                if (savedItem.checklist_item_id != currentItem.id)
                {
                    savedItem.checklist_item_id = currentItem.id;
                    database.Connection.Update(savedItem);
                    referencesChanged = true;
                }

                syncItems.Add(new InventoryRunItemSyncDto
                {
                    itemId = currentItem.id,
                    status = savedItem.status,
                    comment = savedItem.comment ?? string.Empty
                });
            }

            if (referencesChanged)
            {
                run.cabinet_id = cabinet.id;
                run.qr_code_id = cabinet.qr_code_id;
                database.Connection.Update(run);
            }

            dto = new InventoryRunSyncDto
            {
                clientRunId = run.client_run_id,
                clientBatchId = string.IsNullOrWhiteSpace(run.client_batch_id) ? null : run.client_batch_id,
                expectedBoxCount = run.expected_box_count,
                userId = run.user_id,
                qrCode = run.qr_code,
                startedAt = run.started_at,
                completedAt = run.completed_at,
                items = syncItems
            };
            return true;
        }

        /// <summary>Формирует читаемое сообщение о сетевой ошибке и адресе сервера.</summary>
        private static string FormatRequestError(UnityWebRequest request, string baseUrl)
        {
            var detail = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            if (detail != null && detail.Length > 300) detail = detail.Substring(0, 300);
            string result = request.responseCode > 0 ? $"HTTP {request.responseCode}: {request.error}" : request.error;
            if (!string.IsNullOrWhiteSpace(detail)) result += $" — {detail}";
            return $"{result} (API: {baseUrl})";
        }

        /// <summary>Выбирает USB для редактора и Wi-Fi для установленного приложения.</summary>
        private static string InventoryApiDefaultUrl()
        {
            return Application.isEditor ? USB_URL : WIFI_URL;
        }
    }

    /// <summary>Пакет обычных событий локального журнала.</summary>
    [Serializable]
    public class SyncPayload { public List<SyncDto> items = new List<SyncDto>(); }

    /// <summary>Одно событие журнала в формате API.</summary>
    [Serializable]
    public class SyncDto
    {
        public long userId;
        public long qrCodeId;
        public string action;
        public string comment;
        public string actionTime;
        public string fullName;
        public string department;
    }

    /// <summary>Счётчики обработки обычных событий сервером.</summary>
    [Serializable]
    public class SyncResult { public int imported; public int skipped; }

    /// <summary>Пакет завершённых проверок для API.</summary>
    [Serializable]
    public class InventoryRunsSyncPayload { public List<InventoryRunSyncDto> items = new List<InventoryRunSyncDto>(); }

    /// <summary>Проверка инвентаризации вместе с необязательными медиа-вложениями.</summary>
    [Serializable]
    public class InventoryRunSyncDto
    {
        public string clientRunId;
        public string clientBatchId;
        public int expectedBoxCount;
        public long userId;
        public string qrCode;
        public string startedAt;
        public string completedAt;
        public List<InventoryRunItemSyncDto> items;
        public string photoBase64;
        public string[] photosBase64;
        public string voiceBase64;
        public string reportNote;
    }
    /// <summary>Результат одного пункта чек-листа в запросе синхронизации.</summary>
    [Serializable]
    public class InventoryRunItemSyncDto
    {
        public long itemId;
        public string status;
        public int actualQuantity;
        public string comment;
    }

    /// <summary>Результат обработки пакета проверок сервером.</summary>
    [Serializable]
    public class InventoryRunsSyncResult
    {
        public int imported;
        public int duplicates;
        public int skipped;
        public List<string> errors = new List<string>();
    }
}
