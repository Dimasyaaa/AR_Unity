using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Text.RegularExpressions;

namespace ArInventory.Local
{
    public class LocalClient : MonoBehaviour
    {
        public static LocalClient Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public static LocalClient GetOrCreate()
        {
            if (Instance != null)
                return Instance;

            GameObject go = new GameObject("LocalClient");
            go.AddComponent<LocalClient>();
            return Instance;
        }

        public void GetDepartments(Action<List<string>> onSuccess, Action<string> onError)
        {
            if (!LocalDatabase.Instance.IsReady)
            {
                onError?.Invoke("База данных еще не готова");
                return;
            }

            List<string> names = LocalDatabase.Instance.Connection
                .Table<LocalUser>()
                .Where(u => u.is_active)
                .ToList()
                .Select(u => u.department)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            onSuccess?.Invoke(names);
        }

        public void Login(
            string fullName,
            string department,
            string password,
            Action<LoginResponse> onSuccess,
            Action<string> onError)
        {
            if (!LocalDatabase.Instance.IsReady)
            {
                onError?.Invoke("База данных еще не готова");
                return;
            }

            string cleanFullName = CleanString(fullName);
            string cleanDepartment = CleanString(department);

            LocalUser user = LocalDatabase.Instance.Connection
                .Table<LocalUser>()
                .ToList()
                .FirstOrDefault(u =>
                    u.is_active &&
                    CleanString(u.full_name) == cleanFullName &&
                    CleanString(u.department) == cleanDepartment);

            if (user == null)
            {
                onError?.Invoke("Неверный отдел или ФИО");
                return;
            }

            // Проверяем пароль через BCrypt
            bool passwordOk;
            try
            {
                passwordOk = BCrypt.Net.BCrypt.Verify(password, user.password_hash);
            }
            catch (Exception e)
            {
                Debug.LogError("[Login] BCrypt error: " + e.Message);
                onError?.Invoke("Ошибка проверки пароля");
                return;
            }

            if (!passwordOk)
            {
                onError?.Invoke("Неверный пароль");
                return;
            }

            // Записываем действие входа
            var session = new LocalInventorySession
            {
                user_id = user.id,
                action = "login",
                action_time = DateTime.Now,
                comment = "Вход в систему"
            };
            LocalDatabase.Instance.Connection.Insert(session);

            var response = new LoginResponse
            {
                success = true,
                message = "Вход выполнен",
                userId = (int)user.id,
                fullName = user.full_name,
                department = user.department,
                loginActionId = (int)session.id
            };

            SessionManager.SetUser((int)user.id, user.full_name, user.department);
            onSuccess?.Invoke(response);
        }

        public void SendScan(
            string qrCode,
            Action<ScanResponse> onSuccess,
            Action<string> onError)
        {
            if (!LocalDatabase.Instance.IsReady)
            {
                onError?.Invoke("База данных еще не готова");
                return;
            }

            if (!SessionManager.IsLoggedIn)
            {
                onError?.Invoke("Пользователь не авторизован");
                return;
            }

            LocalQrCode qr = LocalDatabase.Instance.Connection
                .Table<LocalQrCode>()
                .ToList()
                .FirstOrDefault(q =>
                    q.is_active &&
                    string.Equals(q.code, qrCode, StringComparison.Ordinal));

            if (qr == null)
            {
                onError?.Invoke($"QR-код {qrCode} не найден");
                return;
            }

            // Проверяем повтор
            bool alreadyScanned = LocalDatabase.Instance.Connection
                .Table<LocalInventorySession>()
                .Where(s =>
                    s.user_id == SessionManager.UserId &&
                    s.qr_code_id == qr.id &&
                    s.action == "scan")
                .Count() > 0;

            var session = new LocalInventorySession
            {
                user_id = SessionManager.UserId,
                qr_code_id = qr.id,
                action = "scan",
                action_time = DateTime.Now,
                comment = alreadyScanned
                    ? "Повторное сканирование QR"
                    : "Сканирование QR"
            };

            LocalDatabase.Instance.Connection.Insert(session);

            var response = new ScanResponse
            {
                success = true,
                message = "QR обработан",
                scanId = (int)session.id,
                objectName = qr.object_name,
                result = alreadyScanned ? "duplicate" : "ok"
            };

            onSuccess?.Invoke(response);
        }

        // Оставляет только буквы (убирает пробелы, точки, регистр)
        private static string CleanString(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";

            string lower = s.ToLowerInvariant();
            return Regex.Replace(lower, "[^a-zа-яё]", "");
        }
    }

    public static class LocalDatabaseExt
    {
        public static LocalDatabase GetOrCreate()
        {
            if (LocalDatabase.Instance != null)
                return LocalDatabase.Instance;

            GameObject go = new GameObject("LocalDatabase");
            go.AddComponent<LocalDatabase>();
            return LocalDatabase.Instance;
        }
    }
}