using System.IO;
using System.Security.Cryptography;
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
            string source = Path.Combine(Application.streamingAssetsPath, "inventory.db");
            PrepareDatabase(source);
            OpenDatabase();
        }

        private System.Collections.IEnumerator InitAndroid()
        {
            string url = "jar:file://" + Application.dataPath + "!/assets/inventory.db";
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
            using (var conn = new SQLiteConnection(DatabasePath))
            {
                conn.Execute("ATTACH DATABASE ? AS newdb", sourcePath);
                conn.Execute("DELETE FROM users");
                conn.Execute("INSERT INTO users SELECT * FROM newdb.users");
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
            try { db.Execute("ALTER TABLE inventory_sessions ADD COLUMN synced INTEGER NOT NULL DEFAULT 0"); }
            catch { /* колонка уже есть — игнорируем */ }

            long usersCount = db.Table<LocalUser>().Count();
            long qrCount = db.Table<LocalQrCode>().Count();

            Debug.Log($"[DB] Ready. Users: {usersCount}, QR: {qrCount}");
            OnDatabaseReady?.Invoke();
        }

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