using UnityEditor;
using UnityEngine;
using System.IO;
using Npgsql;
using SQLite;
using ArInventory.Local;

public class ExportDatabaseTool : EditorWindow
{
    private string connectionString =
        "Host=localhost;Port=5432;Database=ar_inventory;" +
        "Username=postgres;Password=1234;SSL Mode=Disable;";

    [MenuItem("Tools/Export PostgreSQL → SQLite")]
    public static void ShowWindow()
    {
        GetWindow<ExportDatabaseTool>("Export DB");
    }

    private void OnGUI()
    {
        GUILayout.Label("Экспорт базы из PostgreSQL в Unity", EditorStyles.boldLabel);

        EditorGUILayout.LabelField("Строка подключения PostgreSQL:");
        connectionString = EditorGUILayout.TextArea(connectionString, GUILayout.Height(60));

        if (GUILayout.Button("Экспортировать в StreamingAssets/inventory.db"))
        {
            DoExport();
        }
    }

    private void DoExport()
    {
        string outputPath = Path.Combine(
            Application.streamingAssetsPath,
            "inventory.db");

        // Удаляем старый файл
        if (File.Exists(outputPath))
            File.Delete(outputPath);

        try
        {
            using var pg = new NpgsqlConnection(connectionString);
            pg.Open();

            using var sqlite = new SQLiteConnection(outputPath);
            sqlite.CreateTable<LocalUser>();
            sqlite.CreateTable<LocalQrCode>();
            sqlite.CreateTable<LocalInventorySession>();

            // Экспорт users
            using (var cmd = new NpgsqlCommand(
                "SELECT id, full_name, department, password_hash, is_active FROM users",
                pg))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    sqlite.Insert(new LocalUser
                    {
                        id = reader.GetInt64(0),
                        full_name = reader.GetString(1),
                        department = reader.GetString(2),
                        password_hash = reader.GetString(3),
                        is_active = reader.GetBoolean(4)
                    });
                }
            }

            // Экспорт qr_codes
            using (var cmd = new NpgsqlCommand(
                "SELECT id, code, object_name, is_active FROM qr_codes",
                pg))
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    sqlite.Insert(new LocalQrCode
                    {
                        id = reader.GetInt64(0),
                        code = reader.GetString(1),
                        object_name = reader.GetString(2),
                        is_active = reader.GetBoolean(3)
                    });
                }
            }

            AssetDatabase.Refresh();

            long users = sqlite.Table<LocalUser>().Count();
            long qrs = sqlite.Table<LocalQrCode>().Count();

            EditorUtility.DisplayDialog(
                "Экспорт выполнен",
                $"Сохранено:\n{outputPath}\n\nПользователей: {users}\nQR-кодов: {qrs}",
                "OK");
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog("Ошибка", e.Message, "OK");
            Debug.LogError(e);
        }
    }
}