using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using ArInventory.Local;
using UnityEngine;
using UnityEngine.Networking;

namespace ArInventory.Network
{
    /// <summary>Данные коробки, полученные от REST API.</summary>
    [Serializable]
    public sealed class InventoryBoxResponse
    {
        public bool success;
        public long boxId;
        public long qrCodeId;
        public string qrCode;
        public string title;
        public bool photoAvailable;
        public string photoUrl;
        public bool modelAvailable;
        public string modelUrl;
        public InventoryBoxPhoto[] photos;
        public InventoryBoxItem[] items;
    }

    /// <summary>Изображение содержимого коробки из ответа API.</summary>
    [Serializable]
    public sealed class InventoryBoxPhoto
    {
        public int pageNumber;
        public string caption;
        public string contentType;
        public string photoUrl;
    }

    /// <summary>Позиция чек-листа коробки и тип её тестовой 3D-фигуры.</summary>
    [Serializable]
    public sealed class InventoryBoxItem
    {
        public long id;
        public string name;
        public string description;
        public int sortOrder;
        public int expectedQuantity;
        public string modelType;
    }

    /// <summary>Коробка, которую сотруднику разрешено проверить в текущем запуске.</summary>
    [Serializable]
    public sealed class InventoryAssignedBox
    {
        public long qrCodeId;
        public string qrCode;
        public string title;
    }

    /// <summary>Назначенное сервером задание на инвентаризацию выбранных коробок.</summary>
    [Serializable]
    public sealed class InventoryAssignmentResponse
    {
        public bool success;
        public string clientBatchId;
        public int expectedBoxCount;
        public string status;
        public InventoryAssignedBox[] boxes;
    }

    /// <summary>Сводные данные для экрана, где сотрудник выбирает назначение.</summary>
    [Serializable]
    public sealed class InventoryAssignmentOverviewResponse
    {
        public InventoryAssignmentResponse activeAssignment;
        public InventoryAssignmentHistory[] history;
    }

    /// <summary>Короткая строка истории общей инвентаризации сотрудника.</summary>
    [Serializable]
    public sealed class InventoryAssignmentHistory
    {
        public string clientBatchId;
        public int expectedBoxCount;
        public int completedBoxCount;
        public string status;
        public string startedAt;
        public string completedAt;
    }

    /// <summary>
    /// Общая точка связи приложения с сервером инвентаризации.
    /// Загружает содержимое коробки и его изображения, а также отправляет результат проверки.
    /// </summary>
    public static class InventoryApiClient
    {
        private const string BaseAddressKey = "ArInventory.ApiBaseUrl";
        private static string lastLoggedBaseUrl;

        // Используется только этим клиентом: адрес выбирается из сохранённой настройки
        // или из адресов по умолчанию для Unity Editor и Android.
        private static string BaseUrl
        {
            get
            {
                var saved = PlayerPrefs.GetString(BaseAddressKey, string.Empty);
                string resolved;

                if (!string.IsNullOrWhiteSpace(saved))
                {
                    saved = saved.Trim().TrimEnd('/');

                    // Сохранённая настройка переживает обновление приложения. Если USB-туннель
                    // уже отключён, loopback-адрес указывает на сам телефон, поэтому обычные
                    // Запросы содержимого коробок переключаем на Wi-Fi. USB остаётся доступен для синхронизации.
                    resolved = Application.platform == RuntimePlatform.Android && IsLoopback(saved)
                        ? SyncManager.WIFI_URL
                        : saved;
                }
                else
                {
                    resolved = Application.isEditor ? SyncManager.USB_URL : SyncManager.WIFI_URL;
                }

                if (resolved != lastLoggedBaseUrl)
                {
                    lastLoggedBaseUrl = resolved;
                    Debug.Log($"[API] Адрес API для запросов приложения: {resolved}");
                }

                return resolved;
            }
        }

        /// <summary>Получает из API карточку коробки по тексту её QR-кода.</summary>
        public static void GetBox(MonoBehaviour runner, string qrCode,
            Action<InventoryBoxResponse> onSuccess, Action<string> onError)
        {
            runner.StartCoroutine(GetBoxRoutine(qrCode, onSuccess, onError));
        }

        /// <summary>Загружает изображение содержимого коробки.</summary>
        public static void GetBoxPhoto(MonoBehaviour runner, string qrCode, int pageNumber,
            Action<Texture2D> onSuccess, Action<string> onError)
        {
            runner.StartCoroutine(GetBoxPhotoRoutine(qrCode, pageNumber, onSuccess, onError));
        }

        /// <summary>Скачивает GLB-модель коробки, если она загружена в админ-панели.</summary>
        public static void GetBoxModel(MonoBehaviour runner, string qrCode,
            Action<byte[]> onSuccess, Action<string> onError)
        {
            runner.StartCoroutine(GetBoxModelRoutine(qrCode, onSuccess, onError));
        }

        /// <summary>Запрашивает назначенное администратором задание конкретного сотрудника.</summary>
        public static void GetActiveAssignment(MonoBehaviour runner, long userId,
            Action<InventoryAssignmentResponse> onSuccess, Action<string> onError)
        {
            runner.StartCoroutine(GetActiveAssignmentRoutine(userId, onSuccess, onError));
        }

        /// <summary>Загружает активное назначение и историю завершённых инвентаризаций.</summary>
        public static void GetAssignmentOverview(MonoBehaviour runner, long userId,
            Action<InventoryAssignmentOverviewResponse> onSuccess, Action<string> onError)
        {
            runner.StartCoroutine(GetAssignmentOverviewRoutine(userId, onSuccess, onError));
        }

        /// <summary>Отправляет завершённую инвентаризацию на сервер.</summary>
        public static void SubmitInventory(MonoBehaviour runner, InventoryRunSyncDto run,
            Action<InventoryRunsSyncResult> onSuccess, Action<string> onError)
        {
            runner.StartCoroutine(SubmitRoutine(run, onSuccess, onError));
        }

        /// <summary>Сохраняет выбранный адрес API, например адрес USB- или Wi-Fi-подключения.</summary>
        public static void RememberBaseUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return;
            PlayerPrefs.SetString(BaseAddressKey, baseUrl.Trim().TrimEnd('/'));
            PlayerPrefs.Save();
        }

        /// <summary>Выполняет HTTP-запрос содержимого коробки и проверяет ответ сервера.</summary>
        private static IEnumerator GetBoxRoutine(string qrCode,
            Action<InventoryBoxResponse> onSuccess, Action<string> onError)
        {
            string url = $"{BaseUrl}/api/inventory/boxes?qrCode={UnityWebRequest.EscapeURL(qrCode)}";
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 10;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                { onError?.Invoke(ReadableError(request)); yield break; }

                var response = JsonUtility.FromJson<InventoryBoxResponse>(request.downloadHandler.text);
                if (response == null || !response.success)
                { onError?.Invoke("Сервер вернул некорректные данные коробки."); yield break; }
                onSuccess?.Invoke(response);
            }
        }

        /// <summary>Выполняет HTTP-запрос фотографии и преобразует ответ в текстуру Unity.</summary>
        private static IEnumerator GetBoxPhotoRoutine(string qrCode, int pageNumber,
            Action<Texture2D> onSuccess, Action<string> onError)
        {
            string url = $"{BaseUrl}/api/inventory/box-photo?qrCode={UnityWebRequest.EscapeURL(qrCode)}&pageNumber={Mathf.Max(1, pageNumber)}";
            using (var request = UnityWebRequestTexture.GetTexture(url, true))
            {
                request.timeout = 15;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                { onError?.Invoke(ReadableError(request)); yield break; }
                onSuccess?.Invoke(DownloadHandlerTexture.GetContent(request));
            }
        }

        private static IEnumerator GetBoxModelRoutine(string qrCode,
            Action<byte[]> onSuccess, Action<string> onError)
        {
            string url = $"{BaseUrl}/api/inventory/box-model?qrCode={UnityWebRequest.EscapeURL(qrCode)}";
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                { onError?.Invoke(ReadableError(request)); yield break; }
                onSuccess?.Invoke(request.downloadHandler.data);
            }
        }

        private static IEnumerator GetActiveAssignmentRoutine(long userId,
            Action<InventoryAssignmentResponse> onSuccess, Action<string> onError)
        {
            // Без авторизации запрашиваем единственную общую опись без идентификатора сотрудника.
            string url = SessionManager.AuthenticationEnabled
                ? $"{BaseUrl}/api/inventory/assignments/{userId}/active"
                : $"{BaseUrl}/api/inventory/assignments/active";
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 10;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                { onError?.Invoke(ReadableError(request)); yield break; }

                var response = JsonUtility.FromJson<InventoryAssignmentResponse>(request.downloadHandler.text);
                if (response == null || !response.success || response.boxes == null || response.boxes.Length == 0)
                { onError?.Invoke("Сервер вернул некорректное назначение коробок."); yield break; }
                onSuccess?.Invoke(response);
            }
        }

        private static IEnumerator GetAssignmentOverviewRoutine(long userId,
            Action<InventoryAssignmentOverviewResponse> onSuccess, Action<string> onError)
        {
            string url = $"{BaseUrl}/api/inventory/assignments/{userId}/overview";
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 10;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                { onError?.Invoke(ReadableError(request)); yield break; }

                var response = JsonUtility.FromJson<InventoryAssignmentOverviewResponse>(request.downloadHandler.text);
                if (response == null)
                { onError?.Invoke("Сервер вернул некорректный список инвентаризаций."); yield break; }
                response.history ??= Array.Empty<InventoryAssignmentHistory>();
                if (response.activeAssignment != null &&
                    (response.activeAssignment.boxes == null || response.activeAssignment.boxes.Length == 0))
                { onError?.Invoke("В назначении не найдено ни одной коробки."); yield break; }
                onSuccess?.Invoke(response);
            }
        }

        /// <summary>Упаковывает одну проверку в формат пакетной синхронизации и отправляет её.</summary>
        private static IEnumerator SubmitRoutine(InventoryRunSyncDto run,
            Action<InventoryRunsSyncResult> onSuccess, Action<string> onError)
        {
            var payload = new InventoryRunsSyncPayload { items = new List<InventoryRunSyncDto> { run } };
            var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
            using (var request = new UnityWebRequest(BaseUrl + "/api/sync/inventory-runs", "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
                request.timeout = 45;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                { onError?.Invoke(ReadableError(request)); yield break; }

                var response = JsonUtility.FromJson<InventoryRunsSyncResult>(request.downloadHandler.text);
                if (response == null || response.skipped > 0 || response.imported + response.duplicates != 1)
                {
                    var detail = response != null && response.errors != null && response.errors.Count > 0
                        ? response.errors[0]
                        : "Сервер не подтвердил сохранение проверки.";
                    onError?.Invoke(detail);
                    yield break;
                }
                onSuccess?.Invoke(response);
            }
        }

        /// <summary>Добавляет к сетевой ошибке адрес сервера и текст ответа, если он есть.</summary>
        private static string ReadableError(UnityWebRequest request)
        {
            var serverMessage = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            string target = request.url;
            if (Uri.TryCreate(request.url, UriKind.Absolute, out var uri))
                target = uri.GetLeftPart(UriPartial.Authority);

            return string.IsNullOrWhiteSpace(serverMessage)
                ? $"{request.error} (API: {target})"
                : $"{request.error} (API: {target}): {serverMessage}";
        }

        /// <summary>Проверяет, указывает ли сохранённый адрес на это же устройство.</summary>
        private static bool IsLoopback(string address)
        {
            return Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.IsLoopback;
        }
    }
}
