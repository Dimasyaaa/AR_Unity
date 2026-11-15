using System.IO;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using SQLite;

namespace ArInventory.Local
{
    public class LocalDatabase : MonoBehaviour
    {
        public static LocalDatabase Instance { get; private set; }
        public static event System.Action OnDatabaseReady;

        private SQLiteConnection db;
        public bool IsReady { get; private set; }
        public string DatabasePath { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            DatabasePath = Path.Combine(Application.persistentDataPath, "inventory.db");

#if UNITY_ANDROID && !UNITY_EDITOR
            StartCoroutine(InitAndroid());
#else
            InitDesktop();
#endif
        }

        private void InitDesktop()
        {
            string source = Path.Combine(
                Application.streamingAssetsPath,
                "InventoryDatabase",
                "inventory.db");
            PrepareDatabase(source);
            OpenDatabase();
        }

        private System.Collections.IEnumerator InitAndroid()
        {
            string url = "jar:file://" + Application.dataPath +
                         "!/assets/InventoryDatabase/inventory.db";
            string temp = Path.Combine(Application.persistentDataPath, "inventory_new.db");

            using (UnityWebRequest www = UnityWebRequest.Get(url))
            {
                yield return www.SendWebRequest();
                if (www.result == UnityWebRequest.Result.Success)
                {
                    File.WriteAllBytes(temp, www.downloadHandler.data);
                    PrepareDatabase(temp);
                }
                else
                {
                    Debug.LogError("[DB] Failed to copy database: " + www.error);
                }
            }
            OpenDatabase();
        }

        // Копирует или обновляет локальную БД, сохраняя локальные сессии
        private void PrepareDatabase(string sourcePath)
        {
            if (!File.Exists(sourcePath))
            {
                Debug.LogWarning("[DB] inventory.db not found: " + sourcePath);
                return;
            }

            string markerPath = DatabasePath + ".marker";
            string newHash = ComputeHash(sourcePath);

            // Первый запуск — просто копируем
            if (!File.Exists(DatabasePath))
            {
                File.Copy(sourcePath, DatabasePath, overwrite: true);
                File.WriteAllText(markerPath, newHash);
                Debug.Log("[DB] Copied fresh database");
                return;
            }

            string oldHash = File.Exists(markerPath) ? File.ReadAllText(markerPath) : "";
            if (oldHash == newHash)
            {
                Debug.Log("[DB] Database is up to date");
                return;
            }

            // Экспорт изменился — обновляем справочники, сохраняя сессии
            try
            {
                RefreshReferenceData(sourcePath);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[DB] Refresh failed, doing full copy: " + e.Message);
                File.Copy(sourcePath, DatabasePath, overwrite: true);
            }

            File.WriteAllText(markerPath, newHash);
            Debug.Log("[DB] Reference data refreshed from new export");
        }

        // Заменяет users и qr_codes данными из нового экспорта, сессии не трогает
        private void RefreshReferenceData(string sourcePath)
        {
            bool hasCabinetTables;
            using (var source = new SQLiteConnection(sourcePath))
            {
                hasCabinetTables = source.GetTableInfo("cabinet_profiles").Count > 0 &&
                                   source.GetTableInfo("cabinet_checklist_items").Count > 0;
            }

            using (var conn = new SQLiteConnection(DatabasePath))
            {
                conn.Execute("ATTACH DATABASE ? AS newdb", sourcePath);
                conn.Execute("DELETE FROM users");
                conn.Execute("INSERT INTO users SELECT * FROM newdb.users");
                if (hasCabinetTables)
                {
                    conn.Execute("DELETE FROM cabinet_checklist_items");
                    conn.Execute("DELETE FROM cabinet_profiles");
                    conn.Execute("INSERT INTO cabinet_profiles SELECT * FROM newdb.cabinet_profiles");
                    conn.Execute("INSERT INTO cabinet_checklist_items SELECT * FROM newdb.cabinet_checklist_items");
                }
                conn.Execute("DELETE FROM qr_codes");
                conn.Execute("INSERT INTO qr_codes SELECT * FROM newdb.qr_codes");
                conn.Execute("DETACH DATABASE newdb");
            }
        }

        private string ComputeHash(string path)
        {
            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(path))
            {
                var hash = md5.ComputeHash(stream);
                return System.BitConverter.ToString(hash).Replace("-", "");
            }
        }

        private void OpenDatabase()
        {
            SQLitePCL.Batteries_V2.Init();

            if (!File.Exists(DatabasePath))
            {
                Debug.LogError("[DB] inventory.db still missing at " + DatabasePath);
                return;
            }

            db = new SQLiteConnection(DatabasePath);
            IsReady = true;

            db.CreateTable<LocalInventorySession>();
            db.CreateTable<LocalCabinetProfile>();
            db.CreateTable<LocalCabinetChecklistItem>();
            db.CreateTable<LocalInventoryRun>();
            db.CreateTable<LocalInventoryRunItem>();
            try { db.Execute("ALTER TABLE inventory_sessions ADD COLUMN synced INTEGER NOT NULL DEFAULT 0"); }
            catch { /* колонка уже есть — игнорируем */ }

#if UNITY_EDITOR
            EnsureEditorDemoCabinet();
#endif

            long usersCount = db.Table<LocalUser>().Count();
            long qrCount = db.Table<LocalQrCode>().Count();

            Debug.Log($"[DB] Ready. Users: {usersCount}, QR: {qrCount}");
            OnDatabaseReady?.Invoke();
        }

        public LocalCabinetProfile FindCabinetByQr(string qrValue)
        {
            if (!IsReady || string.IsNullOrWhiteSpace(qrValue)) return null;

            var candidates = ExpandQr(qrValue);
            var qr = db.Table<LocalQrCode>()
                .ToList()
                .FirstOrDefault(item => item.is_active && candidates.Contains(item.code));
            if (qr == null) return null;

            return db.Table<LocalCabinetProfile>()
                .ToList()
                .FirstOrDefault(cabinet => cabinet.is_active && cabinet.qr_code_id == qr.id);
        }

        public List<LocalCabinetChecklistItem> GetCabinetChecklist(long cabinetId)
        {
            if (!IsReady) return new List<LocalCabinetChecklistItem>();
            return db.Table<LocalCabinetChecklistItem>()
                .ToList()
                .Where(item => item.cabinet_id == cabinetId && item.is_active)
                .OrderBy(item => item.sort_order)
                .ThenBy(item => item.id)
                .ToList();
        }

        public LocalInventoryRun SaveInventoryRun(
            long cabinetId,
            string qrValue,
            List<LocalInventoryItemResult> results,
            System.DateTime startedAtUtc,
            string clientRunId = null)
        {
            if (!IsReady) throw new System.InvalidOperationException("Локальная база ещё не готова.");
            if (!SessionManager.IsLoggedIn) throw new System.InvalidOperationException("Сначала войдите в приложение.");
            if (results == null || results.Count == 0) throw new System.ArgumentException("Список предметов пуст.");
            if (results.Any(item => item == null || (item.status != "present" && item.status != "missing")))
                throw new System.ArgumentException("Отметьте для каждого предмета «Есть» или «Нет».");
            if (results.Select(item => item.itemId).Distinct().Count() != results.Count)
                throw new System.ArgumentException("В списке есть повторяющиеся предметы.");

            var cabinet = FindCabinetByQr(qrValue);
            if (cabinet == null || cabinet.id != cabinetId)
                throw new System.InvalidOperationException("Данные шкафа не совпадают с текущим QR-кодом.");

            var validIds = GetCabinetChecklist(cabinetId).Select(item => item.id).ToHashSet();
            if (results.Any(item => !validIds.Contains(item.itemId)))
                throw new System.ArgumentException("Список предметов устарел. Обновите данные шкафа.");

            var now = System.DateTime.UtcNow;
            var run = new LocalInventoryRun
            {
                client_run_id = string.IsNullOrWhiteSpace(clientRunId)
                    ? System.Guid.NewGuid().ToString("D")
                    : System.Guid.Parse(clientRunId).ToString("D"),
                cabinet_id = cabinetId,
                qr_code_id = cabinet.qr_code_id,
                user_id = SessionManager.UserId,
                qr_code = ExpandQr(qrValue).FirstOrDefault() ?? qrValue,
                started_at = startedAtUtc.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                completed_at = now.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                synced = false
            };

            db.RunInTransaction(() =>
            {
                db.Insert(run);
                foreach (var result in results)
                {
                    db.Insert(new LocalInventoryRunItem
                    {
                        run_id = run.id,
                        checklist_item_id = result.itemId,
                        item_name_snapshot = result.itemName ?? string.Empty,
                        status = result.status,
                        comment = result.comment ?? string.Empty
                    });
                }

                db.Insert(new LocalInventorySession
                {
                    user_id = SessionManager.UserId,
                    qr_code_id = cabinet.qr_code_id,
                    action = "inventory",
                    action_time = now,
                    comment = $"Инвентаризация «{cabinet.title}»: есть {results.Count(item => item.status == "present")}, нет {results.Count(item => item.status == "missing")}",
                    synced = true
                });
            });

            return run;
        }

        public void MarkInventoryRunSynced(string clientRunId)
        {
            if (!IsReady || string.IsNullOrWhiteSpace(clientRunId)) return;
            var run = db.Table<LocalInventoryRun>()
                .FirstOrDefault(item => item.client_run_id == clientRunId);
            if (run == null) return;
            run.synced = true;
            db.Update(run);
        }

        public static List<string> ExpandQr(string value)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(value)) return result;

            string trimmed = value.Trim();
            result.Add(trimmed);
            if (System.Uri.TryCreate(trimmed, System.UriKind.Absolute, out var uri))
            {
                string tail = System.Uri.UnescapeDataString(
                    uri.AbsolutePath.TrimEnd('/').Split('/').LastOrDefault() ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(tail) && !result.Contains(tail)) result.Add(tail);
            }
            return result;
        }

#if UNITY_EDITOR
        private void EnsureEditorDemoCabinet()
        {
            const string demoCode = "DEMO-CABINET-001";
            var qr = db.Table<LocalQrCode>().ToList().FirstOrDefault(item => item.code == demoCode);
            if (qr == null)
            {
                qr = new LocalQrCode
                {
                    code = demoCode,
                    object_name = "Тестовый шкаф инвентаризации",
                    is_active = true
                };
                db.Insert(qr);
            }

            var cabinet = db.Table<LocalCabinetProfile>()
                .ToList()
                .FirstOrDefault(item => item.qr_code_id == qr.id);
            if (cabinet == null)
            {
                cabinet = new LocalCabinetProfile
                {
                    qr_code_id = qr.id,
                    title = "Тестовый шкаф",
                    reference_photo_content_type = "image/jpeg",
                    is_active = true
                };
                db.Insert(cabinet);
            }

            if (GetCabinetChecklist(cabinet.id).Count > 0) return;
            string[] names =
            {
                "Ключ гаечный 13 мм",
                "Ключ гаечный 17 мм",
                "Перчатки защитные",
                "Переносной фонарь"
            };
            for (int i = 0; i < names.Length; i++)
            {
                db.Insert(new LocalCabinetChecklistItem
                {
                    cabinet_id = cabinet.id,
                    item_name = names[i],
                    description = "Тестовая позиция",
                    sort_order = (i + 1) * 10,
                    is_active = true
                });
            }
        }
#endif

        public SQLiteConnection Connection
        {
            get
            {
                if (!IsReady) Debug.LogError("[DB] Database is not ready");
                return db;
            }
        }

        private void OnDestroy()
        {
            if (db != null) { db.Dispose(); db = null; }
        }
    }
}
