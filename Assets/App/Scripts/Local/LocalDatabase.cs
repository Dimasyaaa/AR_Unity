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
            bool hasBoxModelTable;
            bool hasAssignmentTable;
            bool hasQuantityFields;
            List<LocalBoxModel> exportedModels;
            List<LocalInventoryBatch> exportedAssignments;
            using (var source = new SQLiteConnection(sourcePath))
            {
                hasCabinetTables = source.GetTableInfo("cabinet_profiles").Count > 0 &&
                                   source.GetTableInfo("cabinet_checklist_items").Count > 0;
                hasCabinetPhotoTable = source.GetTableInfo("cabinet_photos").Count > 0;
                hasBoxModelTable = source.GetTableInfo("box_models").Count > 0;
                hasAssignmentTable = source.GetTableInfo("local_inventory_batches").Count > 0;
                exportedModels = hasBoxModelTable
                    ? source.Table<LocalBoxModel>().ToList()
                    : new List<LocalBoxModel>();
                exportedAssignments = hasAssignmentTable
                    ? source.Table<LocalInventoryBatch>().ToList()
                    : new List<LocalInventoryBatch>();
                var sourceItems = source.GetTableInfo("cabinet_checklist_items");
                hasQuantityFields = sourceItems.Any(column => column.Name == "expected_quantity") &&
                                    sourceItems.Any(column => column.Name == "model_type");
            }

            using (var conn = new SQLiteConnection(databasePath))
            {
                conn.CreateTable<LocalCabinetPhoto>();
                conn.CreateTable<LocalCabinetChecklistItem>();
                conn.CreateTable<LocalBoxModel>();
                conn.CreateTable<LocalInventoryBatch>();
                EnsureColumn(conn, "cabinet_checklist_items", "expected_quantity", "INTEGER NOT NULL DEFAULT 1");
                EnsureColumn(conn, "cabinet_checklist_items", "model_type", "TEXT NOT NULL DEFAULT 'cube'");
                EnsureColumn(conn, "local_inventory_batches", "assigned_qr_codes", "TEXT NOT NULL DEFAULT ''");
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
                        if (hasQuantityFields)
                        {
                            conn.Execute(@"INSERT INTO cabinet_checklist_items
                                (id, cabinet_id, item_name, description, sort_order, is_active, expected_quantity, model_type)
                                SELECT id, cabinet_id, item_name, description, sort_order, is_active, expected_quantity, model_type
                                FROM newdb.cabinet_checklist_items");
                        }
                        else
                        {
                            conn.Execute(@"INSERT INTO cabinet_checklist_items
                                (id, cabinet_id, item_name, description, sort_order, is_active, expected_quantity, model_type)
                                SELECT id, cabinet_id, item_name, description, sort_order, is_active, 1, 'cube'
                                FROM newdb.cabinet_checklist_items");
                        }
                    }
                    conn.Execute("DELETE FROM cabinet_photos");
                    if (hasCabinetPhotoTable)
                        conn.Execute("INSERT INTO cabinet_photos SELECT * FROM newdb.cabinet_photos");
                    conn.Execute("DELETE FROM qr_codes");
                    conn.Execute("INSERT INTO qr_codes SELECT * FROM newdb.qr_codes");

                    if (hasBoxModelTable)
                    {
                        conn.Execute("DELETE FROM box_models");
                        foreach (LocalBoxModel model in exportedModels)
                        {
                            model.id = 0;
                            conn.Insert(model);
                        }
                    }

                    // Обновляем назначения из экспорта, но не сбрасываем локально завершённые проверки.
                    foreach (LocalInventoryBatch assignment in exportedAssignments)
                    {
                        if (string.IsNullOrWhiteSpace(assignment.client_batch_id) ||
                            string.IsNullOrWhiteSpace(assignment.assigned_qr_codes))
                            continue;

                        LocalInventoryBatch cached = conn.Table<LocalInventoryBatch>()
                            .ToList()
                            .FirstOrDefault(item => item.client_batch_id == assignment.client_batch_id);
                        if (cached != null && cached.status == "completed")
                            continue;

                        if (cached == null)
                        {
                            assignment.id = 0;
                            assignment.status = "in_progress";
                            conn.Insert(assignment);
                            continue;
                        }

                        cached.user_id = assignment.user_id;
                        cached.expected_box_count = assignment.expected_box_count;
                        cached.assigned_qr_codes = assignment.assigned_qr_codes;
                        cached.started_at = string.IsNullOrWhiteSpace(cached.started_at)
                            ? assignment.started_at
                            : cached.started_at;
                        cached.status = "in_progress";
                        conn.Update(cached);
                    }
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
            db.CreateTable<LocalInventoryBatch>();
            db.CreateTable<LocalBoxModel>();
            EnsureColumn(db, "local_inventory_batches", "assigned_qr_codes", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(db, "cabinet_checklist_items", "expected_quantity", "INTEGER NOT NULL DEFAULT 1");
            EnsureColumn(db, "cabinet_checklist_items", "model_type", "TEXT NOT NULL DEFAULT 'cube'");
            EnsureColumn(db, "local_inventory_run_items", "expected_quantity", "INTEGER NOT NULL DEFAULT 1");
            EnsureColumn(db, "local_inventory_run_items", "actual_quantity", "INTEGER NOT NULL DEFAULT 0");
            EnsureColumn(db, "local_inventory_runs", "box_title", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(db, "local_inventory_runs", "client_batch_id", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(db, "local_inventory_runs", "expected_box_count", "INTEGER NOT NULL DEFAULT 0");
            db.Execute(@"UPDATE local_inventory_run_items
                SET actual_quantity = CASE WHEN status = 'present' THEN expected_quantity ELSE 0 END
                WHERE actual_quantity = 0 AND status = 'present'");
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

        /// <summary>Добавляет колонку в старую локальную базу, только если её там ещё нет.</summary>
        private static void EnsureColumn(SQLiteConnection connection, string tableName,
            string columnName, string columnDefinition)
        {
            if (connection.GetTableInfo(tableName).Any(column => column.Name == columnName))
                return;

            connection.Execute($"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition}");
        }

        /// <summary>Кэширует назначение сервера, чтобы сотрудник мог продолжить работу без сети.</summary>
        public LocalInventoryBatch SaveAssignedInventoryBatch(string clientBatchId, long userId,
            IReadOnlyList<string> assignedQrCodes)
        {
            if (!IsReady || userId <= 0 || string.IsNullOrWhiteSpace(clientBatchId))
                return null;

            var normalizedCodes = assignedQrCodes?
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(GetQrIdentity)
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();
            if (normalizedCodes.Count == 0)
                return null;

            var existing = db.Table<LocalInventoryBatch>()
                .ToList()
                .FirstOrDefault(batch => batch.client_batch_id == clientBatchId);

            // Предыдущее назначение сохраняет свои отчёты, но перестаёт быть активным.
            foreach (var previous in db.Table<LocalInventoryBatch>().ToList()
                         .Where(batch => batch.user_id == userId && batch.status == "in_progress" &&
                                         batch.client_batch_id != clientBatchId))
            {
                previous.status = "cancelled";
                db.Update(previous);
            }

            var batch = existing ?? new LocalInventoryBatch();
            bool wasCompleted = batch.status == "completed";
            batch.client_batch_id = clientBatchId;
            batch.user_id = userId;
            // Число коробок берём из фактического списка QR-кодов администратора.
            batch.expected_box_count = normalizedCodes.Count;
            batch.assigned_qr_codes = string.Join("|", normalizedCodes);
            if (string.IsNullOrWhiteSpace(batch.started_at))
                batch.started_at = System.DateTime.UtcNow.ToString("o");
            batch.status = wasCompleted ? "completed" : "in_progress";
            if (existing == null)
                db.Insert(batch);
            else
                db.Update(batch);

            if (!wasCompleted && GetCompletedBoxCount(clientBatchId) >= batch.expected_box_count)
            {
                batch.status = "completed";
                batch.completed_at = System.DateTime.UtcNow.ToString("o");
                db.Update(batch);
            }
            return batch;
        }

        /// <summary>Сохраняет обновлённый статус назначенного запуска.</summary>
        public void UpdateInventoryBatch(LocalInventoryBatch batch)
        {
            if (!IsReady || batch == null) return;
            db.Update(batch);
        }

        /// <summary>Проверяет, что отсканированная коробка входит в назначенный список QR-кодов.</summary>
        public bool IsQrAssignedToBatch(string clientBatchId, string scannedQrCode)
        {
            if (!IsReady || string.IsNullOrWhiteSpace(clientBatchId) || string.IsNullOrWhiteSpace(scannedQrCode))
                return false;
            var batch = db.Table<LocalInventoryBatch>().ToList()
                .FirstOrDefault(item => item.client_batch_id == clientBatchId);
            if (batch == null || string.IsNullOrWhiteSpace(batch.assigned_qr_codes))
                return false;

            string identity = GetQrIdentity(scannedQrCode);
            return batch.assigned_qr_codes.Split('|')
                .Any(code => System.String.Equals(code, identity, System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Возвращает активную общую проверку сотрудника или начинает новую.</summary>
        public LocalInventoryBatch GetOrStartInventoryBatch(long userId, int expectedBoxCount)
        {
            if (!IsReady || userId <= 0)
                return null;

            int safeCount = System.Math.Max(1, expectedBoxCount);
            LocalInventoryBatch active = db.Table<LocalInventoryBatch>()
                .ToList()
                .FirstOrDefault(batch => batch.user_id == userId && batch.status == "in_progress");

            if (active != null)
            {
                int completed = GetCompletedBoxCount(active.client_batch_id);
                if (completed < active.expected_box_count)
                    return active;

                active.status = "completed";
                active.completed_at = System.DateTime.UtcNow.ToString("o");
                db.Update(active);
            }

            var batch = new LocalInventoryBatch
            {
                client_batch_id = System.Guid.NewGuid().ToString("D"),
                user_id = userId,
                expected_box_count = safeCount,
                started_at = System.DateTime.UtcNow.ToString("o"),
                status = "in_progress"
            };
            db.Insert(batch);
            return batch;
        }

        /// <summary>Возвращает незавершённый запуск сотрудника, если он есть.</summary>
        public LocalInventoryBatch GetActiveInventoryBatch(long userId)
        {
            if (!IsReady || userId <= 0)
                return null;

            return db.Table<LocalInventoryBatch>()
                .ToList()
                .FirstOrDefault(batch => batch.user_id == userId && batch.status == "in_progress");
        }

        /// <summary>Возвращает последний запуск сотрудника для итогового экрана.</summary>
        public LocalInventoryBatch GetLatestInventoryBatch(long userId)
        {
            if (!IsReady || userId <= 0)
                return null;

            return db.Table<LocalInventoryBatch>()
                .ToList()
                .Where(batch => batch.user_id == userId)
                .OrderByDescending(batch => batch.started_at)
                .FirstOrDefault();
        }

        /// <summary>Возвращает последние завершённые проверки, сохранённые на этом устройстве.</summary>
        public List<LocalInventoryBatch> GetCompletedInventoryBatches(long userId, int limit)
        {
            if (!IsReady || userId <= 0 || limit <= 0)
                return new List<LocalInventoryBatch>();

            return db.Table<LocalInventoryBatch>()
                .ToList()
                .Where(batch => batch.user_id == userId && batch.status == "completed")
                .OrderByDescending(batch => string.IsNullOrWhiteSpace(batch.completed_at)
                    ? batch.started_at
                    : batch.completed_at)
                .Take(limit)
                .ToList();
        }

        /// <summary>Считает уникальные коробки, для которых в этом запуске сохранён отчёт.</summary>
        public int GetCompletedBoxCount(string clientBatchId)
        {
            if (!IsReady || string.IsNullOrWhiteSpace(clientBatchId))
                return 0;

            return db.Table<LocalInventoryRun>()
                .ToList()
                .Where(run => run.client_batch_id == clientBatchId)
                .Select(run => GetQrIdentity(run.qr_code))
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Distinct(System.StringComparer.OrdinalIgnoreCase)
                .Count();
        }

        /// <summary>Проверяет, не завершал ли сотрудник эту коробку в текущем запуске.</summary>
        public bool HasCompletedBox(string clientBatchId, string qrCode)
        {
            if (!IsReady || string.IsNullOrWhiteSpace(clientBatchId) || string.IsNullOrWhiteSpace(qrCode))
                return false;

            string identity = GetQrIdentity(qrCode);
            return db.Table<LocalInventoryRun>()
                .ToList()
                .Any(run => run.client_batch_id == clientBatchId &&
                    System.String.Equals(GetQrIdentity(run.qr_code), identity,
                        System.StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Собирает сводные строки общей проверки из локально сохранённых отчётов.</summary>
        public List<LocalInventoryBatchSummaryRow> GetInventoryBatchSummary(string clientBatchId)
        {
            var result = new List<LocalInventoryBatchSummaryRow>();
            if (!IsReady || string.IsNullOrWhiteSpace(clientBatchId))
                return result;

            var runs = db.Table<LocalInventoryRun>()
                .ToList()
                .Where(run => run.client_batch_id == clientBatchId)
                .OrderBy(run => run.completed_at)
                .ToList();
            var allItems = db.Table<LocalInventoryRunItem>().ToList();

            foreach (var run in runs)
            {
                foreach (var item in allItems.Where(item => item.run_id == run.id).OrderBy(item => item.id))
                {
                    string status = item.actual_quantity <= 0 ? "Нет"
                        : item.actual_quantity < item.expected_quantity ? "Частично" : "Есть";
                    result.Add(new LocalInventoryBatchSummaryRow
                    {
                        boxTitle = run.box_title,
                        qrCode = run.qr_code,
                        itemName = item.item_name_snapshot,
                        expectedQuantity = item.expected_quantity,
                        actualQuantity = item.actual_quantity,
                        status = status,
                        completedAt = run.completed_at
                    });
                }
            }
            return result;
        }

        private static string GetQrIdentity(string value)
        {
            List<string> candidates = ExpandQr(value);
            return candidates.Count > 0 ? candidates[candidates.Count - 1].Trim() : string.Empty;
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

        /// <summary>Возвращает GLB-модель коробки из локальной копии базы.</summary>
        public LocalBoxModel GetBoxModel(long cabinetId)
        {
            if (!IsReady || cabinetId <= 0) return null;
            return db.Table<LocalBoxModel>()
                .ToList()
                .FirstOrDefault(model => model.cabinet_id == cabinetId);
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
                box_title = cabinetTitle ?? string.Empty,
                client_batch_id = report.clientBatchId ?? string.Empty,
                expected_box_count = report.expectedBoxCount,
                started_at = report.startedAt,
                completed_at = report.completedAt,
                synced = false,
                report_payload = JsonUtility.ToJson(report)
            };

            db.RunInTransaction(() =>
            {
                if (!string.IsNullOrWhiteSpace(report.clientBatchId) &&
                    !db.Table<LocalInventoryBatch>().ToList().Any(batch => batch.client_batch_id == report.clientBatchId))
                {
                    db.Insert(new LocalInventoryBatch
                    {
                        client_batch_id = report.clientBatchId,
                        user_id = report.userId,
                        expected_box_count = System.Math.Max(1, report.expectedBoxCount),
                        started_at = report.startedAt,
                        status = "in_progress"
                    });
                }

                db.Insert(run);
                foreach (var result in report.items)
                {
                    db.Insert(new LocalInventoryRunItem
                    {
                        run_id = run.id,
                        checklist_item_id = result.itemId,
                        item_name_snapshot = itemNames[result.itemId],
                        status = result.status,
                        expected_quantity = GetExpectedQuantity(result.itemId),
                        actual_quantity = result.actualQuantity,
                        comment = result.comment ?? string.Empty
                    });
                }

                int presentCount = 0;
                int partialCount = 0;
                int missingCount = 0;
                foreach (var result in report.items)
                {
                    int expected = GetExpectedQuantity(result.itemId);
                    if (result.actualQuantity <= 0) missingCount++;
                    else if (result.actualQuantity < expected) partialCount++;
                    else presentCount++;
                }

                db.Insert(new LocalInventorySession
                {
                    user_id = report.userId,
                    qr_code_id = qrCodeId,
                    action = "inventory",
                    action_time = System.DateTime.Parse(report.completedAt, null, System.Globalization.DateTimeStyles.RoundtripKind),
                    comment = $"Инвентаризация «{cabinetTitle}»: есть {presentCount}, частично {partialCount}, нет {missingCount}",
                    synced = true
                });

                if (!string.IsNullOrWhiteSpace(report.clientBatchId))
                {
                    LocalInventoryBatch batch = db.Table<LocalInventoryBatch>()
                        .ToList()
                        .FirstOrDefault(value => value.client_batch_id == report.clientBatchId);
                    if (batch != null && GetCompletedBoxCount(batch.client_batch_id) >= batch.expected_box_count)
                    {
                        batch.status = "completed";
                        batch.completed_at = report.completedAt;
                        db.Update(batch);
                    }
                }
            });

            int GetExpectedQuantity(long itemId)
            {
                LocalCabinetChecklistItem checklistItem = db.Table<LocalCabinetChecklistItem>()
                    .ToList()
                    .FirstOrDefault(item => item.id == itemId);
                return System.Math.Max(1, checklistItem?.expected_quantity ?? 1);
            }

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
        /// <summary>Добавляет тестовую коробку и позиции только в Unity Editor.</summary>
        private void EnsureEditorDemoCabinet()
        {
            const string demoCode = "DEMO-BOX-001";
            var qr = db.Table<LocalQrCode>().ToList().FirstOrDefault(item => item.code == demoCode);
            if (qr == null)
            {
                qr = new LocalQrCode
                {
                    code = demoCode,
                    object_name = "Демо коробка 1",
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
                    title = "Демо коробка 1",
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
