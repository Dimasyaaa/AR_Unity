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
                        sessionSummary = $"Журнал: импортировано {res.imported}, пропущено {res.skipped}";
                    }
                    else
                    {
                        onDone?.Invoke("Ошибка синхронизации журнала: " + req.error);
                        yield break;
                    }
                }
            }

            var pendingRuns = LocalDatabase.Instance.Connection.Table<LocalInventoryRun>()
                .Where(run => !run.synced).ToList();
            if (pendingRuns.Count == 0) { onDone?.Invoke(sessionSummary); yield break; }

            var runPayload = new InventoryRunsSyncPayload();
            runPayload.items = pendingRuns.Select(run => new InventoryRunSyncDto
            {
                clientRunId = run.client_run_id,
                userId = run.user_id,
                qrCode = run.qr_code,
                startedAt = run.started_at,
                completedAt = run.completed_at,
                items = LocalDatabase.Instance.Connection.Table<LocalInventoryRunItem>()
                    .Where(item => item.run_id == run.id)
                    .ToList()
                    .Select(item => new InventoryRunItemSyncDto
                    {
                        itemId = item.checklist_item_id,
                        status = item.status,
                        comment = item.comment ?? ""
                    }).ToList()
            }).ToList();

            using (var req = new UnityWebRequest(baseUrl + "/api/sync/inventory-runs", "POST"))
            {
                req.SetRequestHeader("Content-Type", "application/json");
                req.downloadHandler = new DownloadHandlerBuffer();
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(runPayload)));
                req.timeout = 10;
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                { onDone?.Invoke("" + sessionSummary + ". Инвентаризация: " + req.error); yield break; }

                var result = JsonUtility.FromJson<InventoryRunsSyncResult>(req.downloadHandler.text);
                if (result == null || result.skipped > 0 || result.imported + result.duplicates != pendingRuns.Count)
                {
                    string detail = result != null && result.errors != null && result.errors.Count > 0
                        ? " " + result.errors[0]
                        : (result != null ? $" Пропущено: {result.skipped}." : string.Empty);
                    onDone?.Invoke($"{sessionSummary}. Сервер не принял все проверки.{detail} Исправьте данные и повторите синхронизацию.");
                    yield break;
                }
                foreach (var run in pendingRuns)
                { run.synced = true; LocalDatabase.Instance.Connection.Update(run); }
                onDone?.Invoke($"{sessionSummary}. Инвентаризация: отправлено {result.imported}, уже было {result.duplicates}, пропущено {result.skipped}");
            }
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
