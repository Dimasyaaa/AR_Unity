using System;
using System.IO;
using System.Linq;
using System.Text;
using Npgsql;
using SQLite;
using UnityEditor;
using UnityEngine;
using ArInventory.Local;

/// <summary>Показывает содержимое PostgreSQL и создаёт локальную SQLite-базу для Unity.</summary>
public class DatabaseExportWindow : EditorWindow
{
    private const string DatabaseFolderName = "InventoryDatabase";
    private const string DatabaseFileName = "inventory.db";

    private string host = "localhost";
    private int port = 5432;
    private string databaseName = "ar_inventory";
    private string username = "postgres";
    private string password = string.Empty;
    private string statisticsText = "Нажмите «Обновить сводку», чтобы посмотреть данные PostgreSQL.";
    private Vector2 statisticsScrollPosition;

    [MenuItem("Tools/Database/Export PostgreSQL to SQLite")]
    public static void ShowWindow()
    {
        GetWindow<DatabaseExportWindow>("Экспорт базы");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Экспорт PostgreSQL → SQLite", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Пароль вводится только в этом окне и не сохраняется в проекте. " +
            "Экспорт включает справочники коробок, общую активную опись, прежние назначения и GLB-модели.",
            MessageType.Info);

        host = EditorGUILayout.TextField("Сервер", host);
        port = EditorGUILayout.IntField("Порт", port);
        databaseName = EditorGUILayout.TextField("База PostgreSQL", databaseName);
        username = EditorGUILayout.TextField("Пользователь", username);
        password = EditorGUILayout.PasswordField("Пароль", password);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(
            "Файл назначения",
            $"Assets/StreamingAssets/{DatabaseFolderName}/{DatabaseFileName}");

        bool isValid = !string.IsNullOrWhiteSpace(host) &&
                       !string.IsNullOrWhiteSpace(databaseName) &&
                       !string.IsNullOrWhiteSpace(username) &&
                       port > 0 && port <= 65535;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Содержимое базы", EditorStyles.boldLabel);
        statisticsScrollPosition = EditorGUILayout.BeginScrollView(
            statisticsScrollPosition, GUILayout.MinHeight(100), GUILayout.MaxHeight(260));
        EditorGUILayout.HelpBox(statisticsText, MessageType.None);
        EditorGUILayout.EndScrollView();
        EditorGUI.BeginDisabledGroup(!isValid);
        if (GUILayout.Button("Обновить сводку"))
            RefreshStatistics();
        if (GUILayout.Button("Экспортировать базу"))
            ExportDatabase();
        EditorGUI.EndDisabledGroup();
    }

    private void RefreshStatistics()
    {
        try
        {
            using var postgres = OpenPostgresConnection();
            postgres.Open();
            statisticsText = ReadStatistics(postgres).ToDisplayText();
        }
        catch (Exception exception)
        {
            statisticsText = "Не удалось прочитать базу: " + exception.Message;
            Debug.LogError("Не удалось получить сводку базы данных: " + exception.Message);
        }
    }

    private void ExportDatabase()
    {
        string outputDirectory = Path.Combine(Application.streamingAssetsPath, DatabaseFolderName);
        string outputPath = Path.Combine(outputDirectory, DatabaseFileName);
        string temporaryPath = Path.Combine(outputDirectory, DatabaseFileName + ".tmp");

        if (!EditorUtility.DisplayDialog(
                "Подтвердить экспорт",
                "После успешного экспорта локальная база будет обновлена справочниками и активными назначениями. " +
                "Локальная история на устройстве останется при следующем запуске приложения.",
                "Продолжить",
                "Отмена"))
            return;

        try
        {
            Directory.CreateDirectory(outputDirectory);
            DeleteIfExists(temporaryPath);

            ExportStatistics statistics;
            using (var postgres = OpenPostgresConnection())
            {
                postgres.Open();
                statistics = ReadStatistics(postgres);

                using (var sqlite = new SQLiteConnection(temporaryPath))
                {
                    sqlite.CreateTable<LocalUser>();
                    sqlite.CreateTable<LocalQrCode>();
                    sqlite.CreateTable<LocalInventorySession>();
                    sqlite.CreateTable<LocalCabinetProfile>();
                    sqlite.CreateTable<LocalCabinetPhoto>();
                    sqlite.CreateTable<LocalCabinetChecklistItem>();
                    sqlite.CreateTable<LocalInventoryBatch>();
                    sqlite.CreateTable<LocalBoxModel>();

                    ExportUsers(postgres, sqlite);
                    ExportQrCodes(postgres, sqlite);
                    ExportBoxes(postgres, sqlite);
                    ExportBoxPhotos(postgres, sqlite);
                    ExportBoxItems(postgres, sqlite);
                    ExportBoxModels(postgres, sqlite);
                    ExportActiveAssignments(postgres, sqlite);

                    ValidateExportRelations(sqlite);

                    statistics.users = sqlite.Table<LocalUser>().Count();
                    statistics.activeUsers = sqlite.Table<LocalUser>().ToList().Count(user => user.is_active);
                    statistics.qrCodes = sqlite.Table<LocalQrCode>().Count();
                    statistics.activeQrCodes = sqlite.Table<LocalQrCode>().ToList().Count(qr => qr.is_active);
                    statistics.boxes = sqlite.Table<LocalCabinetProfile>().Count();
                    statistics.positions = sqlite.Table<LocalCabinetChecklistItem>().ToList().Count(item => item.is_active);
                    statistics.models = sqlite.Table<LocalBoxModel>().Count();
                    statistics.photos = sqlite.Table<LocalCabinetPhoto>().Count() +
                                        sqlite.Table<LocalCabinetProfile>().ToList().Count(box => box.reference_photo != null);
                    statistics.activeAssignments = sqlite.Table<LocalInventoryBatch>()
                        .ToList().Count(batch => batch.status == "in_progress");
                }
            }

            ReplaceDatabase(temporaryPath, outputPath);
            AssetDatabase.Refresh();

            statisticsText = statistics.ToDisplayText();
            EditorUtility.DisplayDialog(
                "Экспорт выполнен",
                $"Локальная база обновлена:\n{outputPath}\n\n{statisticsText}",
                "OK");
        }
        catch (Exception exception)
        {
            DeleteIfExists(temporaryPath);
            Debug.LogError($"Экспорт базы данных не выполнен: {exception.Message}");
            EditorUtility.DisplayDialog("Ошибка экспорта", exception.Message, "OK");
        }
    }

    private NpgsqlConnection OpenPostgresConnection()
    {
        var settings = new NpgsqlConnectionStringBuilder
        {
            Host = host.Trim(),
            Port = port,
            Database = databaseName.Trim(),
            Username = username.Trim(),
            Password = password,
            SslMode = SslMode.Disable
        };
        return new NpgsqlConnection(settings.ConnectionString);
    }

    private static ExportStatistics ReadStatistics(NpgsqlConnection postgres)
    {
        string boxesRelation = ResolveRelation(postgres, "boxes", "cabinet_profiles");
        string itemsRelation = ResolveRelation(postgres, "box_items", "cabinet_checklist_items");
        string photosRelation = ResolveRelation(postgres, "box_photos", "cabinet_photos");
        var result = new ExportStatistics
        {
            users = Count(postgres, "SELECT COUNT(*) FROM users"),
            activeUsers = Count(postgres, "SELECT COUNT(*) FROM users WHERE is_active = TRUE"),
            qrCodes = Count(postgres, "SELECT COUNT(*) FROM qr_codes"),
            activeQrCodes = Count(postgres, "SELECT COUNT(*) FROM qr_codes WHERE is_active = TRUE"),
            boxes = Count(postgres, $"SELECT COUNT(*) FROM {boxesRelation}"),
            positions = Count(postgres, $"SELECT COUNT(*) FROM {itemsRelation} WHERE is_active = TRUE"),
            photos = Count(postgres, $"SELECT COUNT(*) FROM {photosRelation}"),
            models = GetModelCount(postgres),
            usesLegacyBoxSchema = !ColumnExists(postgres, "boxes", "model_data")
        };

        if (TableExists(postgres, "inventory_batches") && TableExists(postgres, "inventory_batch_boxes"))
        {
            result.activeAssignments = Count(postgres, @"
                SELECT COUNT(*) FROM inventory_batches
                WHERE status IN ('assigned', 'in_progress')");

            using var command = new NpgsqlCommand(@"
                SELECT employee.full_name,
                       COUNT(DISTINCT batch.id)::BIGINT,
                       COUNT(DISTINCT batch.id) FILTER (
                           WHERE batch.status IN ('assigned', 'in_progress'))::BIGINT,
                       COUNT(DISTINCT run.id)::BIGINT,
                       COUNT(DISTINCT assigned.qr_code_id) FILTER (
                           WHERE batch.status IN ('assigned', 'in_progress'))::BIGINT
                FROM users employee
                LEFT JOIN inventory_batches batch
                    ON batch.user_id = employee.id
                LEFT JOIN inventory_batch_boxes assigned ON assigned.inventory_batch_id = batch.id
                LEFT JOIN inventory_runs run ON run.batch_id = batch.id
                GROUP BY employee.id, employee.full_name
                ORDER BY employee.full_name", postgres);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                result.assignmentLines.Add(
                    $"{reader.GetString(0)} — назначений: {reader.GetInt64(1)}, активных: {reader.GetInt64(2)}, " +
                    $"завершено проверок коробок: {reader.GetInt64(3)}, коробок в активных: {reader.GetInt64(4)}");
        }

        return result;
    }

    private static bool TableExists(NpgsqlConnection postgres, string tableName)
    {
        using var command = new NpgsqlCommand("SELECT to_regclass(@tableName) IS NOT NULL", postgres);
        command.Parameters.AddWithValue("tableName", "public." + tableName);
        return (bool)command.ExecuteScalar();
    }

    /// <summary>Проверяет наличие столбца в таблице или представлении PostgreSQL.</summary>
    private static bool ColumnExists(NpgsqlConnection postgres, string relationName, string columnName)
    {
        using var command = new NpgsqlCommand(@"
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = @relationName AND column_name = @columnName
            )", postgres);
        command.Parameters.AddWithValue("relationName", relationName);
        command.Parameters.AddWithValue("columnName", columnName);
        return (bool)command.ExecuteScalar();
    }

    /// <summary>Выбирает новое имя таблицы или его имя из прежней схемы.</summary>
    private static string ResolveRelation(NpgsqlConnection postgres, string currentName, string legacyName)
    {
        if (TableExists(postgres, currentName)) return currentName;
        if (TableExists(postgres, legacyName)) return legacyName;
        throw new InvalidOperationException(
            $"В PostgreSQL не найдена таблица или представление {currentName} / {legacyName}.");
    }

    /// <summary>Считает GLB-модели как в новой схеме, так и в прежней таблице моделей.</summary>
    private static long GetModelCount(NpgsqlConnection postgres)
    {
        if (ColumnExists(postgres, "boxes", "model_data"))
            return Count(postgres, "SELECT COUNT(*) FROM boxes WHERE model_data IS NOT NULL");

        return TableExists(postgres, "box_models")
            ? Count(postgres, "SELECT COUNT(*) FROM box_models WHERE model_data IS NOT NULL")
            : 0;
    }

    private static long Count(NpgsqlConnection postgres, string query)
    {
        using var command = new NpgsqlCommand(query, postgres);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void ExportUsers(NpgsqlConnection postgres, SQLiteConnection sqlite)
    {
        using var command = new NpgsqlCommand(
                   "SELECT id, full_name, department, password_hash, is_active FROM users",
                   postgres);
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                sqlite.Execute(
                    "INSERT INTO users (id, full_name, department, password_hash, is_active) VALUES (?, ?, ?, ?, ?)",
                    Convert.ToInt64(reader.GetValue(0)), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetBoolean(4));
            }
        }
    }

    private static void ExportQrCodes(NpgsqlConnection postgres, SQLiteConnection sqlite)
    {
        using var command = new NpgsqlCommand(
            "SELECT id, code, object_name, is_active FROM qr_codes", postgres);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            sqlite.Execute(
                "INSERT INTO qr_codes (id, code, object_name, is_active) VALUES (?, ?, ?, ?)",
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3));
        }
    }

    private static void ExportBoxes(NpgsqlConnection postgres, SQLiteConnection sqlite)
    {
        // На устройстве сохраняем старые имена таблиц SQLite, чтобы не ломать
        // уже установленный локальный кэш и очередь несинхронизированных действий.
        string source = ResolveRelation(postgres, "boxes", "cabinet_profiles");
        using var command = new NpgsqlCommand($@"
            SELECT id, qr_code_id, title, is_active
            FROM {source} ORDER BY id", postgres);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            sqlite.Execute(@"
                INSERT INTO cabinet_profiles
                    (id, qr_code_id, title, reference_photo, reference_photo_content_type, is_active)
                VALUES (?, ?, ?, ?, ?, ?)",
                reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2),
                null, "image/jpeg", reader.GetBoolean(3));
        }
    }

    private static void ExportBoxPhotos(NpgsqlConnection postgres, SQLiteConnection sqlite)
    {
        // Каждое фото из PostgreSQL переносится в совместимую локальную таблицу изображений.
        string source = ResolveRelation(postgres, "box_photos", "cabinet_photos");
        string boxIdColumn = ColumnExists(postgres, source, "box_id") ? "box_id" : "cabinet_id";
        using var command = new NpgsqlCommand($@"
            SELECT photo.id, photo.{boxIdColumn}, photo.page_number, photo.image_data, photo.content_type, photo.caption
            FROM {source} photo
            ORDER BY photo.{boxIdColumn}, photo.page_number", postgres);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            sqlite.Execute(@"
                INSERT INTO cabinet_photos
                    (id, cabinet_id, page_number, image_data, content_type, caption)
                VALUES (?, ?, ?, ?, ?, ?)",
                reader.GetInt64(0), reader.GetInt64(1), Convert.ToInt32(reader.GetValue(2)),
                (byte[])reader.GetValue(3), reader.GetString(4), reader.GetString(5));
        }
    }

    private static void ExportBoxItems(NpgsqlConnection postgres, SQLiteConnection sqlite)
    {
        string source = ResolveRelation(postgres, "box_items", "cabinet_checklist_items");
        string boxIdColumn = ColumnExists(postgres, source, "box_id") ? "box_id" : "cabinet_id";
        string nameColumn = ColumnExists(postgres, source, "name") ? "name" : "item_name";
        string descriptionColumn = ColumnExists(postgres, source, "description") ? "description" : "NULL::TEXT";
        string quantityColumn = ColumnExists(postgres, source, "expected_quantity") ? "expected_quantity" : "1";
        string modelColumn = ColumnExists(postgres, source, "model_type") ? "model_type" : "'cube'";
        using var command = new NpgsqlCommand($@"
            SELECT id, {boxIdColumn} AS box_id, {nameColumn} AS name, {descriptionColumn} AS description,
                   sort_order, is_active, {quantityColumn} AS expected_quantity, {modelColumn} AS model_type
            FROM {source}
            ORDER BY {boxIdColumn}, sort_order, id", postgres);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            sqlite.Execute(@"
                INSERT INTO cabinet_checklist_items
                    (id, cabinet_id, item_name, description, sort_order, is_active, expected_quantity, model_type)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2),
                reader.IsDBNull(3) ? string.Empty : reader.GetString(3), reader.GetInt32(4),
                reader.GetBoolean(5), reader.GetInt32(6), reader.GetString(7));
        }
    }

    private static void ExportBoxModels(NpgsqlConnection postgres, SQLiteConnection sqlite)
    {
        bool modelStoredOnBox = ColumnExists(postgres, "boxes", "model_data");
        string query = modelStoredOnBox
            ? @"SELECT id, id, model_file_name, model_content_type, model_data
                FROM boxes WHERE model_data IS NOT NULL ORDER BY id"
            : @"SELECT id, cabinet_id, file_name, content_type, model_data
                FROM box_models WHERE model_data IS NOT NULL ORDER BY id";
        using var command = new NpgsqlCommand(query, postgres);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            sqlite.Execute(@"
                INSERT INTO box_models (id, cabinet_id, file_name, content_type, model_data)
                VALUES (?, ?, ?, ?, ?)",
                reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2),
                reader.GetString(3), (byte[])reader.GetValue(4));
        }
    }

    private static void ExportActiveAssignments(NpgsqlConnection postgres, SQLiteConnection sqlite)
    {
        if (!TableExists(postgres, "inventory_batches") || !TableExists(postgres, "inventory_batch_boxes"))
            return;

        // Экспортируем активные проверки, включая общую опись без назначенного сотрудника.
        using var command = new NpgsqlCommand(@"
            SELECT batch.client_batch_id, batch.user_id, COUNT(assigned.qr_code_id)::INTEGER,
                   batch.started_at, STRING_AGG(qr.code, '|' ORDER BY qr.code)
            FROM inventory_batches batch
            JOIN inventory_batch_boxes assigned ON assigned.inventory_batch_id = batch.id
            JOIN qr_codes qr ON qr.id = assigned.qr_code_id
            WHERE batch.status IN ('assigned', 'in_progress')
            GROUP BY batch.id, batch.client_batch_id, batch.user_id, batch.started_at
            ORDER BY batch.started_at", postgres);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            // Ноль — локальный признак описи общего терминала без привязки к сотруднику.
            long localUserId = reader.IsDBNull(1) ? 0L : reader.GetInt64(1);
            sqlite.Execute(@"
                INSERT INTO local_inventory_batches
                    (client_batch_id, user_id, expected_box_count, assigned_qr_codes, started_at, status)
                VALUES (?, ?, ?, ?, ?, 'in_progress')",
                reader.GetGuid(0).ToString("D"), localUserId, reader.GetInt32(2),
                reader.GetString(4), reader.GetDateTime(3).ToUniversalTime().ToString("o"));
        }
    }

    private static void ValidateExportRelations(SQLiteConnection sqlite)
    {
        int boxesWithoutQr = sqlite.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM cabinet_profiles box
            LEFT JOIN qr_codes qr ON qr.id = box.qr_code_id WHERE qr.id IS NULL");
        int itemsWithoutBox = sqlite.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM cabinet_checklist_items item
            LEFT JOIN cabinet_profiles box ON box.id = item.cabinet_id WHERE box.id IS NULL");
        int photosWithoutBox = sqlite.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM cabinet_photos photo
            LEFT JOIN cabinet_profiles box ON box.id = photo.cabinet_id WHERE box.id IS NULL");
        int modelsWithoutBox = sqlite.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM box_models model
            LEFT JOIN cabinet_profiles box ON box.id = model.cabinet_id WHERE box.id IS NULL");

        if (boxesWithoutQr > 0 || itemsWithoutBox > 0 || photosWithoutBox > 0 || modelsWithoutBox > 0)
        {
            throw new InvalidOperationException(
                $"В исходной базе нарушены связи: коробок без QR-кода — {boxesWithoutQr}, " +
                $"позиций без коробки — {itemsWithoutBox}, изображений без коробки — {photosWithoutBox}, " +
                $"GLB без коробки — {modelsWithoutBox}. Экспорт отменён.");
        }
    }

    private static void ReplaceDatabase(string temporaryPath, string outputPath)
    {
        if (!File.Exists(outputPath))
        {
            File.Move(temporaryPath, outputPath);
            return;
        }

        string backupPath = outputPath + ".backup-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Replace(temporaryPath, outputPath, backupPath);
        }
        finally
        {
            DeleteIfExists(backupPath);
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private sealed class ExportStatistics
    {
        public long users;
        public long activeUsers;
        public long qrCodes;
        public long activeQrCodes;
        public long boxes;
        public long positions;
        public long photos;
        public long models;
        public long activeAssignments;
        public bool usesLegacyBoxSchema;
        public readonly System.Collections.Generic.List<string> assignmentLines = new();

        public string ToDisplayText()
        {
            var text = new StringBuilder();
            text.AppendLine($"Пользователи: {users} (активных: {activeUsers})");
            text.AppendLine($"QR-коды: {qrCodes} (активных: {activeQrCodes})");
            text.AppendLine($"Коробки: {boxes}");
            text.AppendLine($"Позиции перечня: {positions}");
            text.AppendLine($"Изображения содержимого: {photos}");
            text.AppendLine($"GLB-модели: {models}");
            text.AppendLine($"Активные назначения: {activeAssignments}");
            if (usesLegacyBoxSchema)
            {
                text.AppendLine();
                text.AppendLine(
                    "Схема PostgreSQL ещё старая: экспорт читает данные из прежних таблиц. " +
                    "Для перехода на 11 таблиц сначала сделайте pg_dump, затем выполните " +
                    "Backend/Database/update_database_to_boxes.sql.");
            }
            if (assignmentLines.Count > 0)
            {
                text.AppendLine("По сотрудникам:");
                foreach (string assignment in assignmentLines) text.AppendLine("  · " + assignment);
            }
            else
            {
                text.Append("По сотрудникам: активных назначений нет.");
            }
            return text.ToString().TrimEnd();
        }
    }
}
