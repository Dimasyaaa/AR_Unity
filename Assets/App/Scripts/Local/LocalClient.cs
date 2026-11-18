using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System.Text.RegularExpressions;

namespace ArInventory.Local
{
    /// <summary>Проверяет вход и записывает пользовательские события в локальную базу.</summary>
    public class LocalClient : MonoBehaviour
    {
        /// <summary>Единственный постоянно работающий клиент локальных данных.</summary>
        public static LocalClient Instance { get; private set; }

        /// <summary>Оставляет один экземпляр клиента при переходах между сценами.</summary>
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

        /// <summary>Возвращает существующий клиент или создаёт его при необходимости.</summary>
        public static LocalClient GetOrCreate()
        {
            if (Instance != null)
                return Instance;

            GameObject go = new GameObject("LocalClient");
            go.AddComponent<LocalClient>();
            return Instance;
        }

        /// <summary>Получает список отделов из активных пользователей локальной базы.</summary>
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

        /// <summary>Проверяет пользователя и пароль по локальной базе, чтобы вход работал офлайн.</summary>
        public void Login(
            string fullName,
            string department,
            string password,
            Action<LocalLoginResult> onSuccess,
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

            // Пароль хранится как BCrypt-хеш; сравниваем его, не восстанавливая исходный текст.
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

            // Вход сохраняется в локальную очередь для общего журнала сервера.
            var session = new LocalInventorySession
            {
                user_id = user.id,
                action = "login",
                action_time = DateTime.Now,
                comment = "Вход в систему"
            };
            LocalDatabase.Instance.Connection.Insert(session);

            var response = new LocalLoginResult
            {
                userId = (int)user.id,
                fullName = user.full_name,
                department = user.department
            };

            SessionManager.SetUser((int)user.id, user.full_name);
            onSuccess?.Invoke(response);
        }

        /// <summary>Добавляет сканирование QR-кода в локальный журнал.</summary>
        public void SendScan(
            string qrCode,
            Action<LocalScanResult> onSuccess,
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

            var candidates = LocalDatabase.ExpandQr(qrCode);
            LocalQrCode qr = LocalDatabase.Instance.Connection
                .Table<LocalQrCode>()
                .ToList()
                .FirstOrDefault(q => q.is_active && candidates.Contains(q.code));

            if (qr == null)
            {
                onError?.Invoke($"QR-код {qrCode} не найден");
                return;
            }

            // Повтором считается сканирование того же QR этим же пользователем.
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

            var response = new LocalScanResult
            {
                objectName = qr.object_name,
                wasRepeated = alreadyScanned
            };

            onSuccess?.Invoke(response);
        }

        /// <summary>Записывает завершённый расчёт как действие calc в локальный журнал.</summary>
        public void LogCalculator(
            string expression,
            string result,
            Action onSuccess,
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

            // Если для калькулятора создан отдельный QR, прикрепляем его к записи журнала.
            LocalQrCode calcQr = LocalDatabase.Instance.Connection
                .Table<LocalQrCode>()
                .ToList()
                .FirstOrDefault(q => q.code == "CALC_001");

            var session = new LocalInventorySession
            {
                user_id = SessionManager.UserId,
                qr_code_id = calcQr?.id,
                action = "calc",
                action_time = DateTime.Now,
                comment = $"Калькулятор: {expression} = {result}"
            };

            LocalDatabase.Instance.Connection.Insert(session);
            onSuccess?.Invoke();
        }

        /// <summary>Убирает пробелы и знаки препинания для сравнения ФИО и названий отделов.</summary>
        private static string CleanString(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";

            string lower = s.ToLowerInvariant();
            return Regex.Replace(lower, "[^a-zа-яё]", "");
        }
    }

}
