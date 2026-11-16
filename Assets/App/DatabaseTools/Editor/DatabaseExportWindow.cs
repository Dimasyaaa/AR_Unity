using System;
using System.IO;
using Npgsql;
using SQLite;
using UnityEditor;
using UnityEngine;
using ArInventory.Local;

public class DatabaseExportWindow : EditorWindow
{
    private const string DatabaseFolderName = "InventoryDatabase";
    private const string DatabaseFileName = "inventory.db";

    private string host = "localhost";
    private int port = 5432;
    private string databaseName = "ar_inventory";
    private string username = "postgres";
    private string password = string.Empty;

    [MenuItem("Tools/Database/Export PostgreSQL to SQLite")]
    public static void ShowWindow()
    {
        GetWindow<DatabaseExportWindow>("Database Export");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Экспорт PostgreSQL → SQLite", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Пароль вводится только в этом окне и не сохраняется в проекте. " +
            "Текущая база заменяется только после успешного экспорта.",
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

        EditorGUI.BeginDisabledGroup(!isValid);
        if (GUILayout.Button("Экспортировать базу"))
            ExportDatabase();
        EditorGUI.EndDisabledGroup();
    }

    private void ExportDatabase()
    {
        string outputDirectory = Path.Combine(
            Application.streamingAssetsPath,
            DatabaseFolderName);
        string outputPath = Path.Combine(outputDirectory, DatabaseFileName);
        string temporaryPath = Path.Combine(outputDirectory, DatabaseFileName + ".tmp");

        if (!EditorUtility.DisplayDialog(
                "Подтвердить экспорт",
                "После успешного экспорта локальная база будет заменена новыми справочниками.",
                "Продолжить",
                "Отмена"))
            return;

        try
        {
            Directory.CreateDirectory(outputDirectory);
            DeleteIfExists(temporaryPath);

            var settings = new NpgsqlConnectionStringBuilder
            {
                Host = host.Trim(),
                Port = port,
                Database = databaseName.Trim(),
                Username = username.Trim(),
                Password = password,
                SslMode = SslMode.Disable
            };

            long usersCount;
            long qrCount;
            long cabinetCount;
            long secondPagePhotoCount;
            long checklistItemCount;

            using (var postgres = new NpgsqlConnection(settings.ConnectionString))
            {
                postgres.Open();

                using (var sqlite = new SQLiteConnection(temporaryPath))
                {
                    sqlite.CreateTable<LocalUser>();
                    sqlite.CreateTable<LocalQrCode>();
                    sqlite.CreateTable<LocalInventorySession>();
                    sqlite.CreateTable<LocalCabinetProfile>();
                    sqlite.CreateTable<LocalCabinetPhoto>();
                    sqlite.CreateTable<LocalCabinetChecklistItem>();

                    ExportUsers(postgres, sqlite);
                    ExportQrCodes(postgres, sqlite);
                    ExportCabinets(postgres, sqlite);
                    ExportCabinetPhotos(postgres, sqlite);
                    ExportChecklistItems(postgres, sqlite);

                    ValidateExportRelations(sqlite);

                    usersCount = sqlite.Table<LocalUser>().Count();
                    qrCount = sqlite.Table<LocalQrCode>().Count();
                    cabinetCount = sqlite.Table<LocalCabinetProfile>().Count();
                    secondPagePhotoCount = sqlite.Table<LocalCabinetPhoto>().Count();
                    checklistItemCount = sqlite.Table<LocalCabinetChecklistItem>().Count();
                }
            }

            ReplaceDatabase(temporaryPath, outputPath);
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "Экспорт выполнен",
                $"Файл обновлён:\n{outputPath}\n\nПользователей: {usersCount}\nQR-кодов: {qrCount}\nШкафов: {cabinetCount}\nФото листа 2: {secondPagePhotoCount}\nПунктов перечня: {checklistItemCount}",
                "OK");
        }
        catch (Exception exception)
        {
            DeleteIfExists(temporaryPath);
            Debug.LogError($"Экспорт базы данных не выполнен: {exception.Message}");
            EditorUtility.DisplayDialog("Ошибка экспорта", exception.Message, "OK");
        }
    }

    private static void ExportUsers(NpgsqlConnection postgres, SQLiteConnection sqlite)
    {
        using (var command = new NpgsqlCommand(
                   "SELECT id, full_name, department, password_hash, is_active FROM users",
                   postgres))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                // SQLite-net omits auto-increment primary keys during Insert, even
                // when the source id is assigned. Preserve PostgreSQL ids explicitly
                // because other exported rows refer to them.
                sqlite.Execute(
                    "INSERT INTO users (id, full_name, department, password_hash, is_active) VALUES (?, ?, ?, ?, ?)",
                    Convert.ToInt64(reader.GetValue(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetBoolean(4));
            }
        }
    }

    private static void ExportQrCodes(NpgsqlConnection postgres, SQLiteConnection sqlite)
    {
        using (var command = new NpgsqlCommand(
                   "SELECT id, code, object_name, is_active FROM qr_codes",
                   postgres))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                sqlite.Execute(
                    "INSERT INTO qr_codes (id, code, object_name, is_active) VALUES (?, ?, ?, ?)",
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetBoolean(3));
            }
        }
    }

    private static void ExportCabinets(NpgsqlConnection postgres, SQLiteConnection sqlite)
    {
        using (var command = new NpgsqlCommand(@"
                   SELECT id, qr_code_id, title, reference_photo,
                          reference_photo_content_type, is_active
                   FROM cabinet_profiles
                   ORDER BY id
               ", postgres))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                sqlite.Execute(
                    @"INSERT INTO cabinet_profiles
                      (id, qr_code_id, title, reference_photo, reference_photo_content_type, is_active)
                      VALUES (?, ?, ?, ?, ?, ?)",
                    reader.GetInt64(0),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : (byte[])reader.GetValue(3),
                    reader.IsDBNull(4) ? "image/jpeg" : reader.GetString(4),
                    reader.GetBoolean(5));
            }
        }
    }

    private static void ExportChecklistItems(NpgsqlConnection postgres, SQLiteConnection sqlite)
    {
        using (var command = new NpgsqlCommand(@"
                   SELECT id, cabinet_id, item_name, description, sort_order, is_active
                   FROM cabinet_checklist_items
                   ORDER BY cabinet_id, sort_order, id
               ", postgres))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                sqlite.Execute(
                    @"INSERT INTO cabinet_checklist_items
                      (id, cabinet_id, item_name, description, sort_order, is_active)
                      VALUES (?, ?, ?, ?, ?, ?)",
                    reader.GetInt64(0),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetBoolean(5));
            }
        }
    }

    private static void ValidateExportRelations(SQLiteConnection sqlite)
    {
        int cabinetsWithoutQr = sqlite.ExecuteScalar<int>(@"
            SELECT COUNT(*)
            FROM cabinet_profiles cabinet
            LEFT JOIN qr_codes qr ON qr.id = cabinet.qr_code_id
            WHERE qr.id IS NULL");
        int itemsWithoutCabinet = sqlite.ExecuteScalar<int>(@"
            SELECT COUNT(*)
            FROM cabinet_checklist_items item
            LEFT JOIN cabinet_profiles cabinet ON cabinet.id = item.cabinet_id
            WHERE cabinet.id IS NULL");
        int photosWithoutCabinet = sqlite.ExecuteScalar<int>(@"
            SELECT COUNT(*)
            FROM cabinet_photos photo
            LEFT JOIN cabinet_profiles cabinet ON cabinet.id = photo.cabinet_id
            WHERE cabinet.id IS NULL");

        if (cabinetsWithoutQr > 0 || itemsWithoutCabinet > 0 || photosWithoutCabinet > 0)
        {
            throw new InvalidOperationException(
                $"В исходной базе нарушены связи: шкафов без QR-кода — {cabinetsWithoutQr}, " +
                $"пунктов без шкафа — {itemsWithoutCabinet}, фото без шкафа — {photosWithoutCabinet}. Экспорт отменён.");
        }
    }

    private static void ExportCabinetPhotos(NpgsqlConnection postgres, SQLiteConnection sqlite)
    {
        bool photoTableExists;
        using (var exists = new NpgsqlCommand("SELECT to_regclass('public.cabinet_photos') IS NOT NULL", postgres))
            photoTableExists = (bool)exists.ExecuteScalar();

        if (!photoTableExists) return;

        // The first page is already stored in cabinet_profiles.reference_photo.
        // Export page 2 separately to avoid duplicating large image blobs in the APK.
        using (var command = new NpgsqlCommand(@"
                   SELECT id, cabinet_id, page_number, image_data, content_type, caption
                   FROM cabinet_photos
                   WHERE page_number = 2
                   ORDER BY cabinet_id
               ", postgres))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                sqlite.Execute(
                    @"INSERT INTO cabinet_photos
                      (id, cabinet_id, page_number, image_data, content_type, caption)
                      VALUES (?, ?, ?, ?, ?, ?)",
                    reader.GetInt64(0),
                    reader.GetInt64(1),
                    reader.GetInt16(2),
                    (byte[])reader.GetValue(3),
                    reader.GetString(4),
                    reader.GetString(5));
            }
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
        if (File.Exists(path))
            File.Delete(path);
    }
}
