using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using ArInventory.Local;
using UnityEngine;
using UnityEngine.Networking;

namespace ArInventory.Network
{
    /// <summary>Данные шкафа, полученные от API.</summary>
    [Serializable]
    public sealed class InventoryCabinetResponse
    {
        public bool success;
        public long cabinetId;
        public long qrCodeId;
        public string qrCode;
        public string title;
        public bool photoAvailable;
        public string photoUrl;
        public InventoryCabinetPhoto[] photos;
        public InventoryCabinetItem[] items;
    }

    /// <summary>Описание одной фотографии или листа шкафа из ответа API.</summary>
    [Serializable]
    public sealed class InventoryCabinetPhoto
    {
        public int pageNumber;
        public string caption;
        public string contentType;
        public string photoUrl;
    }

    /// <summary>Пункт чек-листа шкафа из ответа API.</summary>
    [Serializable]
    public sealed class InventoryCabinetItem
    {
        public long id;
        public string name;
        public string description;
        public int sortOrder;
    }

    /// <summary>
    /// Общая точка связи приложения с сервером инвентаризации.
    /// Загружает шкаф и его фотографии, а также отправляет результат проверки.
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
                    // запросы к шкафам переключаем на Wi-Fi. USB остаётся доступен для синхронизации.
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

        /// <summary>Запрашивает состав шкафа и передаёт распознанный ответ вызывающему компоненту.</summary>
        public static void GetCabinet(MonoBehaviour runner, string qrCode,
            Action<InventoryCabinetResponse> onSuccess, Action<string> onError)
        {
            runner.StartCoroutine(GetCabinetRoutine(qrCode, onSuccess, onError));
        }

        /// <summary>Загружает изображение заданного листа шкафа.</summary>
        public static void GetPhoto(MonoBehaviour runner, string qrCode, int pageNumber,
            Action<Texture2D> onSuccess, Action<string> onError)
        {
            runner.StartCoroutine(GetPhotoRoutine(qrCode, pageNumber, onSuccess, onError));
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

        /// <summary>Выполняет HTTP-запрос состава шкафа и проверяет ответ сервера.</summary>
        private static IEnumerator GetCabinetRoutine(string qrCode,
            Action<InventoryCabinetResponse> onSuccess, Action<string> onError)
        {
            string url = $"{BaseUrl}/api/inventory/cabinets?qrCode={UnityWebRequest.EscapeURL(qrCode)}";
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = 10;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                { onError?.Invoke(ReadableError(request)); yield break; }

                var response = JsonUtility.FromJson<InventoryCabinetResponse>(request.downloadHandler.text);
                if (response == null || !response.success)
                { onError?.Invoke("Сервер вернул некорректные данные шкафа."); yield break; }
                onSuccess?.Invoke(response);
            }
        }

        /// <summary>Выполняет HTTP-запрос фотографии и преобразует ответ в текстуру Unity.</summary>
        private static IEnumerator GetPhotoRoutine(string qrCode, int pageNumber,
            Action<Texture2D> onSuccess, Action<string> onError)
        {
            string url = $"{BaseUrl}/api/inventory/cabinet-photo?qrCode={UnityWebRequest.EscapeURL(qrCode)}&pageNumber={Mathf.Clamp(pageNumber, 1, 2)}";
            using (var request = UnityWebRequestTexture.GetTexture(url, true))
            {
                request.timeout = 15;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                { onError?.Invoke(ReadableError(request)); yield break; }
                onSuccess?.Invoke(DownloadHandlerTexture.GetContent(request));
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
