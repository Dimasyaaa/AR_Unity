using System.IO;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using SQLite;

namespace ArInventory.Local
{
    /// <summary>Открывает локальную SQLite-базу и предоставляет офлайн-данные приложения.</summary>
    public class LocalDatabase : MonoBehaviour
    {
        /// <summary>Единственный активный экземпляр локальной базы данных.</summary>
        public static LocalDatabase Instance { get; private set; }

        /// <summary>Событие вызывается после создания таблиц и подготовки базы к запросам.</summary>
        public static event System.Action OnDatabaseReady;

        /// <summary>Возвращает текущую базу или создаёт её, если её ещё нет в сцене.</summary>
        public static LocalDatabase GetOrCreate()
        {
            if (Instance != null)
                return Instance;

            var databaseObject = new GameObject("LocalDatabase");
            return databaseObject.AddComponent<LocalDatabase>();
        }

        private SQLiteConnection db;
        private string databasePath;

        /// <summary>Показывает, завершена ли подготовка локальной базы.</summary>
        public bool IsReady { get; private set; }

        /// <summary>Открывает базу в постоянном хранилище приложения.</summary>
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            databasePath = Path.Combine(Application.persistentDataPath, "inventory.db");

#if UNITY_ANDROID && !UNITY_EDITOR
            StartCoroutine(InitAndroid());
#else
            InitDesktop();
#endif
        }

        /// <summary>Подготавливает базу из StreamingAssets в редакторе и на компьютере.</summary>
        private void InitDesktop()
        {
            string source = Path.Combine(
                Application.streamingAssetsPath,
                "InventoryDatabase",
                "inventory.db");
            PrepareDatabase(source);
            OpenDatabase();
        }

        /// <summary>Копирует базу из Android APK во внутреннее хранилище приложения.</summary>
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
                    Debug.LogError("[БД] Не удалось скопировать базу данных: " + www.error);
                }
            }
            OpenDatabase();
        }

        /// <summary>Копирует новую базу или обновляет справочники, сохраняя локальную историю.</summary>
        private void PrepareDatabase(string sourcePath)
        {
            if (!File.Exists(sourcePath))
            {
                Debug.LogWarning("[DB] inventory.db not found: " + sourcePath);
                return;
            }

            string markerPath = databasePath + ".marker";
            string newHash = ComputeHash(sourcePath);

            // Первый запуск — просто копируем
            if (!File.Exists(databasePath))
            {
                File.Copy(sourcePath, databasePath, overwrite: true);
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

            // Экспорт изменился — обновляем справочники, сохраняя локальную историю.
            try
            {
                RefreshReferenceData(sourcePath);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[DB] Не удалось обновить справочники. Сохранена текущая база: " + e.Message);
                return;
            }

            File.WriteAllText(markerPath, newHash);
            Debug.Log("[DB] Reference data refreshed from new export");
        }

        /// <summary>Заменяет данные справочников, не удаляя сохранённые пользователем события и отчёты.</summary>
        private void RefreshReferenceData(string sourcePath)
        {
            bool hasCabinetTables;
            bool hasCabinetPhotoTable;
            using (var source = new SQLiteConnection(sourcePath))
            {
                hasCabinetTables = source.GetTableInfo("cabinet_profiles").Count > 0 &&
                                   source.GetTableInfo("cabinet_checklist_items").Count > 0;
                hasCabinetPhotoTable = source.GetTableInfo("cabinet_photos").Count > 0;
            }

            using (var conn = new SQLiteConnection(databasePath))
            {
                conn.CreateTable<LocalCabinetPhoto>();
                conn.Execute("ATTACH DATABASE ? AS newdb", sourcePath);
                conn.RunInTransaction(() =>
                {
                    conn.Execute("DELETE FROM users");
                    conn.Execute("INSERT INTO users SELECT * FROM newdb.users");
                    if (hasCabinetTables)
                    {
                        conn.Execute("DELETE FROM cabinet_checklist_items");
                        conn.Execute("DELETE FROM cabinet_profiles");
                        conn.Execute("INSERT INTO cabinet_profiles SELECT * FROM newdb.cabinet_profiles");
                        conn.Execute("INSERT INTO cabinet_checklist_items SELECT * FROM newdb.cabinet_checklist_items");
                    }
                    conn.Execute("DELETE FROM cabinet_photos");
                    if (hasCabinetPhotoTable)
                        conn.Execute("INSERT INTO cabinet_photos SELECT * FROM newdb.cabinet_photos");
                    conn.Execute("DELETE FROM qr_codes");
                    conn.Execute("INSERT INTO qr_codes SELECT * FROM newdb.qr_codes");
                });
                conn.Execute("DETACH DATABASE newdb");
            }
        }

        /// <summary>Считает отпечаток экспортированной базы, чтобы не обновлять её без изменений.</summary>
        private string ComputeHash(string path)
        {
            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(path))
            {
                var hash = md5.ComputeHash(stream);
                return System.BitConverter.ToString(hash).Replace("-", "");
            }
        }

        /// <summary>Создаёт локальные таблицы, применяет обновления схемы и сообщает о готовности базы.</summary>
        private void OpenDatabase()
        {
            SQLitePCL.Batteries_V2.Init();

            if (!File.Exists(databasePath))
            {
                Debug.LogError("[DB] inventory.db still missing at " + databasePath);
                return;
            }

            db = new SQLiteConnection(databasePath);
            IsReady = true;

            db.CreateTable<LocalInventorySession>();
            db.CreateTable<LocalCabinetProfile>();
            db.CreateTable<LocalCabinetPhoto>();
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
            long cabinetCount = db.Table<LocalCabinetProfile>().Count();
            long checklistCount = db.Table<LocalCabinetChecklistItem>().Count();

            Debug.Log($"[DB] Ready. Users: {usersCount}, QR: {qrCount}, cabinets: {cabinetCount}, checklist items: {checklistCount}");
            OnDatabaseReady?.Invoke();
        }

        /// <summary>Ищет активный шкаф по QR-коду или последнему сегменту QR-ссылки.</summary>
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

        /// <summary>Возвращает активные пункты чек-листа в заданном порядке.</summary>
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

        /// <summary>Возвращает фотографии листов шкафа в порядке их отображения.</summary>
        public List<LocalCabinetPhoto> GetCabinetPhotos(long cabinetId)
        {
            if (!IsReady) return new List<LocalCabinetPhoto>();
            return db.Table<LocalCabinetPhoto>()
                .ToList()
                .Where(photo => photo.cabinet_id == cabinetId)
                .OrderBy(photo => photo.page_number)
                .ToList();
        }

        /// <summary>Сохраняет отчёт и отметки пунктов одной транзакцией, чтобы не оставить половину записи.</summary>
        public void SaveInventoryRun(InventoryRunSyncDto report, long cabinetId, long qrCodeId,
            string cabinetTitle, IReadOnlyDictionary<long, string> itemNames)
        {
            if (!IsReady) throw new System.InvalidOperationException("Локальная база ещё не готова.");
            var run = new LocalInventoryRun
            {
                client_run_id = report.clientRunId,
                cabinet_id = cabinetId,
                qr_code_id = qrCodeId,
                user_id = report.userId,
                qr_code = report.qrCode,
                started_at = report.startedAt,
                completed_at = report.completedAt,
                synced = false,
                report_payload = JsonUtility.ToJson(report)
            };

            db.RunInTransaction(() =>
            {
                db.Insert(run);
                foreach (var result in report.items)
                {
                    db.Insert(new LocalInventoryRunItem
                    {
                        run_id = run.id,
                        checklist_item_id = result.itemId,
                        item_name_snapshot = itemNames[result.itemId],
                        status = result.status,
                        comment = result.comment ?? string.Empty
                    });
                }

                db.Insert(new LocalInventorySession
                {
                    user_id = report.userId,
                    qr_code_id = qrCodeId,
                    action = "inventory",
                    action_time = System.DateTime.Parse(report.completedAt, null, System.Globalization.DateTimeStyles.RoundtripKind),
                    comment = $"Инвентаризация «{cabinetTitle}»: есть {report.items.Count(item => item.status == "present")}, нет {report.items.Count(item => item.status == "missing")}",
                    synced = true
                });
            });

        }

        /// <summary>Помечает отчёт синхронизированным и очищает локальную копию медиа-пакета.</summary>
        public void MarkInventoryRunSynced(string clientRunId)
        {
            if (!IsReady || string.IsNullOrWhiteSpace(clientRunId)) return;
            db.Execute("UPDATE local_inventory_runs SET synced = 1, report_payload = NULL WHERE client_run_id = ?", clientRunId);
        }

        /// <summary>Для QR-ссылки возвращает саму ссылку и её последний сегмент.</summary>
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
        /// <summary>Добавляет тестовый шкаф и пункты чек-листа только в Unity Editor.</summary>
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

        /// <summary>Возвращает подключение SQLite после подготовки базы.</summary>
        public SQLiteConnection Connection
        {
            get
            {
                if (!IsReady) Debug.LogError("[DB] Database is not ready");
                return db;
            }
        }

        /// <summary>Закрывает SQLite-подключение при завершении приложения.</summary>
        private void OnDestroy()
        {
            if (db != null) { db.Dispose(); db = null; }
        }
    }
}
