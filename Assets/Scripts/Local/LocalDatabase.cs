using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using SQLite;

namespace ArInventory.Local
{
    public class LocalDatabase : MonoBehaviour
    {
        public static LocalDatabase Instance { get; private set; }

        private SQLiteConnection db;
        public bool IsReady { get; private set; }

        // Путь к рабочей БД (туда можно писать)
        public string DatabasePath { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            DatabasePath = Path.Combine(Application.persistentDataPath, "inventory.db");

            // На Android StreamingAssets лежит в jar, нельзя читать как файл —
            // поэтому копируем через UnityWebRequest
#if UNITY_ANDROID && !UNITY_EDITOR
            StartCoroutine(CopyDatabaseFromStreamingAssetsAndroid());
#else
            CopyDatabaseFromStreamingAssetsDesktop();
#endif
        }

        private void CopyDatabaseFromStreamingAssetsDesktop()
        {
            string source = Path.Combine(Application.streamingAssetsPath, "inventory.db");

            if (!File.Exists(DatabasePath))
            {
                if (File.Exists(source))
                {
                    File.Copy(source, DatabasePath, overwrite: false);
                    Debug.Log($"[DB] Copied from StreamingAssets: {source}");
                }
                else
                {
                    Debug.LogWarning("[DB] inventory.db not found in StreamingAssets");
                }
            }

            OpenDatabase();
        }

        private System.Collections.IEnumerator CopyDatabaseFromStreamingAssetsAndroid()
        {
            string url = "jar:file://" + Application.dataPath + "!/assets/inventory.db";

            if (!File.Exists(DatabasePath))
            {
                using (UnityWebRequest www = UnityWebRequest.Get(url))
                {
                    yield return www.SendWebRequest();

                    if (www.result == UnityWebRequest.Result.Success)
                    {
                        File.WriteAllBytes(DatabasePath, www.downloadHandler.data);
                        Debug.Log($"[DB] Copied from StreamingAssets (Android): {DatabasePath}");
                    }
                    else
                    {
                        Debug.LogError("[DB] Failed to copy database: " + www.error);
                    }
                }
            }

            OpenDatabase();
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

            // Гарантируем, что таблица inventory_sessions существует
            db.CreateTable<LocalInventorySession>();

            long usersCount = db.Table<LocalUser>().Count();
            long qrCount = db.Table<LocalQrCode>().Count();

            Debug.Log($"[DB] Ready. Users: {usersCount}, QR: {qrCount}");
        }

        public SQLiteConnection Connection
        {
            get
            {
                if (!IsReady)
                    Debug.LogError("[DB] Database is not ready");
                return db;
            }
        }

        private void OnDestroy()
        {
            if (db != null)
            {
                db.Dispose();
                db = null;
            }
        }
    }
}