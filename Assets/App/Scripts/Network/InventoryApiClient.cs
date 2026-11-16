using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using ArInventory.Local;
using UnityEngine;
using UnityEngine.Networking;

namespace ArInventory.Network
{
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

    [Serializable]
    public sealed class InventoryCabinetPhoto
    {
        public int pageNumber;
        public string caption;
        public string contentType;
        public string photoUrl;
    }

    [Serializable]
    public sealed class InventoryCabinetItem
    {
        public long id;
        public string name;
        public string description;
        public int sortOrder;
    }

    /// <summary>Small HTTP bridge for cabinet data, gallery photos and live inventory submission.</summary>
    public static class InventoryApiClient
    {
        private const string BaseAddressKey = "ArInventory.ApiBaseUrl";
        private static string lastLoggedBaseUrl;

        public static string BaseUrl
        {
            get
            {
                var saved = PlayerPrefs.GetString(BaseAddressKey, string.Empty);
                string resolved;

                if (!string.IsNullOrWhiteSpace(saved))
                {
                    saved = saved.Trim().TrimEnd('/');

                    // PlayerPrefs survives app updates. A previously selected USB address
                    // (127.0.0.1) points back to the phone after the cable/reverse tunnel is gone.
                    // Keep USB available for an explicit Sync action, but use Wi-Fi for the
                    // regular cabinet/photo/checklist API calls on Android.
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

        public static void GetCabinet(MonoBehaviour runner, string qrCode,
            Action<InventoryCabinetResponse> onSuccess, Action<string> onError)
        {
            runner.StartCoroutine(GetCabinetRoutine(qrCode, onSuccess, onError));
        }

        public static void GetPhoto(MonoBehaviour runner, string qrCode, int pageNumber,
            Action<Texture2D> onSuccess, Action<string> onError)
        {
            runner.StartCoroutine(GetPhotoRoutine(qrCode, pageNumber, onSuccess, onError));
        }

        public static void SubmitInventory(MonoBehaviour runner, InventoryRunSyncDto run,
            Action<InventoryRunsSyncResult> onSuccess, Action<string> onError)
        {
            runner.StartCoroutine(SubmitRoutine(run, onSuccess, onError));
        }

        public static void RememberBaseUrl(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return;
            PlayerPrefs.SetString(BaseAddressKey, baseUrl.Trim().TrimEnd('/'));
            PlayerPrefs.Save();
        }

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
                request.timeout = 15;
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

        private static bool IsLoopback(string address)
        {
            return Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.IsLoopback;
        }
    }
}
