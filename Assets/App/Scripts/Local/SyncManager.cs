using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace ArInventory.Local
{
    // Отправляет несинхронизированные локальные сессии на сервер.
    public class SyncManager : MonoBehaviour
    {
        public static SyncManager Instance { get; private set; }

        // USB: телефон подключён кабелем и выполнена команда `adb reverse tcp:8000 tcp:8000`
        public const string USB_URL = "http://127.0.0.1:8000";

        // Wi-Fi: локальный IP компьютера
        public const string WIFI_URL = "http://192.168.0.51:8000";

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public static SyncManager GetOrCreate()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("SyncManager");
            go.AddComponent<SyncManager>();
            return Instance;
        }

        public void SyncNow(string baseUrl, Action<string> onDone)
        {
            StartCoroutine(SyncRoutine(baseUrl, onDone));
        }

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

            var pendingRuns = LocalDatabase.Instance.Connection.Table<LocalInventoryRun>()
                .Where(run => !run.synced).ToList();
            if (pendingRuns.Count == 0) { onDone?.Invoke(sessionSummary); yield break; }

            int importedRuns = 0;
            int duplicateRuns = 0;
            int rejectedRuns = 0;
            string firstRejection = null;

            // Send records one by one. One obsolete local check (for example, a QR code
            // removed from the current reference database) must not prevent valid checks
            // from reaching the server.
            foreach (var run in pendingRuns)
            {
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
                    req.timeout = 10;
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

                    run.synced = true;
                    LocalDatabase.Instance.Connection.Update(run);
                    importedRuns += result.imported;
                    duplicateRuns += result.duplicates;
                }
            }

            string inventorySummary = $"Инвентаризация: отправлено {importedRuns}, уже было {duplicateRuns}";
            if (rejectedRuns > 0)
                inventorySummary += $", оставлено на устройстве {rejectedRuns}. Причина: {firstRejection}";
            onDone?.Invoke($"{sessionSummary}. {inventorySummary}");
        }

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
                userId = run.user_id,
                qrCode = run.qr_code,
                startedAt = run.started_at,
                completedAt = run.completed_at,
                items = syncItems
            };
            return true;
        }

        private static string FormatRequestError(UnityWebRequest request, string baseUrl)
        {
            var detail = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            if (detail != null && detail.Length > 300) detail = detail.Substring(0, 300);
            string result = request.responseCode > 0 ? $"HTTP {request.responseCode}: {request.error}" : request.error;
            if (!string.IsNullOrWhiteSpace(detail)) result += $" — {detail}";
            return $"{result} (API: {baseUrl})";
        }

        private static string InventoryApiDefaultUrl()
        {
            return Application.isEditor ? USB_URL : WIFI_URL;
        }
    }

    [Serializable]
    public class SyncPayload { public List<SyncDto> items = new List<SyncDto>(); }

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

    [Serializable]
    public class SyncResult { public int imported; public int skipped; }

    [Serializable]
    public class InventoryRunsSyncPayload { public List<InventoryRunSyncDto> items = new List<InventoryRunSyncDto>(); }
    [Serializable]
    public class InventoryRunSyncDto
    {
        public string clientRunId;
        public long userId;
        public string qrCode;
        public string startedAt;
        public string completedAt;
        public List<InventoryRunItemSyncDto> items;
    }
    [Serializable]
    public class InventoryRunItemSyncDto { public long itemId; public string status; public string comment; }
    [Serializable]
    public class InventoryRunsSyncResult
    {
        public int imported;
        public int duplicates;
        public int skipped;
        public List<string> errors = new List<string>();
    }
}
