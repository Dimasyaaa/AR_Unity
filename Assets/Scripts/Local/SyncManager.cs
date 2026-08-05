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
        public const string WIFI_URL = "http://10.62.181.230:8000";

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

            if (unsynced.Count == 0)
            { onDone?.Invoke("Нет новых записей"); yield break; }

            var payload = new SyncPayload();
            payload.items = unsynced.Select(s =>
            {
                // Достаём ФИО и отдел, чтобы сервер сам нашёл свой id (не зависит от ids)
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

                    // Читаем реальный результат с сервера
                    var res = JsonUtility.FromJson<SyncResult>(req.downloadHandler.text);
                    onDone?.Invoke($"Импортировано: {res.imported}, пропущено: {res.skipped}");
                }
                else
                {
                    onDone?.Invoke("Ошибка: " + req.error);
                }
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
}