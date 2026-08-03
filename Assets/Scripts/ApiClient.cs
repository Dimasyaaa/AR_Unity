using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

// Клиент для общения с C# сервером (ASP.NET Core).
public class ApiClient : MonoBehaviour
{
    public static ApiClient Instance { get; private set; }

    [Header("API")]
    public string baseUrl = "http://127.0.0.1:8000";

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

    // Создает ApiClient, если его еще нет в сцене.
    public static ApiClient GetOrCreate()
    {
        if (Instance != null)
            return Instance;

        GameObject go = new GameObject("ApiClient");
        go.AddComponent<ApiClient>();

        return Instance;
    }

    public void GetDepartments(Action<List<string>> onSuccess, Action<string> onError)
    {
        StartCoroutine(GetDepartmentsRoutine(onSuccess, onError));
    }

    public void Login(
        string fullName,
        string department,
        string password,
        Action<LoginResponse> onSuccess,
        Action<string> onError)
    {
        StartCoroutine(LoginRoutine(fullName, department, password, onSuccess, onError));
    }

    public void SendScan(
        string qrCode,
        Action<ScanResponse> onSuccess,
        Action<string> onError)
    {
        StartCoroutine(SendScanRoutine(qrCode, onSuccess, onError));
    }

    private IEnumerator GetDepartmentsRoutine(
        Action<List<string>> onSuccess,
        Action<string> onError)
    {
        using (UnityWebRequest www = UnityWebRequest.Get($"{baseUrl}/api/departments"))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke($"Нет связи с сервером: {www.error}");
                yield break;
            }

            DepartmentsResponse response =
                JsonUtility.FromJson<DepartmentsResponse>(www.downloadHandler.text);

            List<string> names = new List<string>();

            if (response != null && response.departments != null)
            {
                foreach (DepartmentItem item in response.departments)
                {
                    names.Add(item.name);
                }
            }

            onSuccess?.Invoke(names);
        }
    }

    private IEnumerator LoginRoutine(
        string fullName,
        string department,
        string password,
        Action<LoginResponse> onSuccess,
        Action<string> onError)
    {
        LoginRequest payload = new LoginRequest
        {
            fullName = fullName,
            department = department,
            password = password
        };

        using (UnityWebRequest www = CreatePostRequest(
            $"{baseUrl}/api/auth/login",
            JsonUtility.ToJson(payload)))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(ExtractErrorMessage(www));
                yield break;
            }

            LoginResponse response =
                JsonUtility.FromJson<LoginResponse>(www.downloadHandler.text);

            if (response != null && response.success)
            {
                SessionManager.SetUser(
                    response.userId,
                    response.fullName,
                    response.department);

                onSuccess?.Invoke(response);
            }
            else
            {
                onError?.Invoke(
                    response != null
                        ? response.message
                        : "Не удалось разобрать ответ сервера");
            }
        }
    }

    private IEnumerator SendScanRoutine(
        string qrCode,
        Action<ScanResponse> onSuccess,
        Action<string> onError)
    {
        if (!SessionManager.IsLoggedIn)
        {
            onError?.Invoke("Пользователь не авторизован");
            yield break;
        }

        ScanRequest payload = new ScanRequest
        {
            userId = SessionManager.UserId,
            qrCode = qrCode
        };

        using (UnityWebRequest www = CreatePostRequest(
            $"{baseUrl}/api/scans",
            JsonUtility.ToJson(payload)))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(ExtractErrorMessage(www));
                yield break;
            }

            ScanResponse response =
                JsonUtility.FromJson<ScanResponse>(www.downloadHandler.text);

            onSuccess?.Invoke(response);
        }
    }

    private UnityWebRequest CreatePostRequest(string url, string json)
    {
        UnityWebRequest www = new UnityWebRequest(url, "POST");

        byte[] body = Encoding.UTF8.GetBytes(json);

        www.uploadHandler = new UploadHandlerRaw(body);
        www.downloadHandler = new DownloadHandlerBuffer();
        www.SetRequestHeader("Content-Type", "application/json");

        return www;
    }

    private string ExtractErrorMessage(UnityWebRequest www)
    {
        if (www.downloadHandler != null &&
            !string.IsNullOrEmpty(www.downloadHandler.text))
        {
            ErrorResponse error =
                JsonUtility.FromJson<ErrorResponse>(www.downloadHandler.text);

            if (error != null && !string.IsNullOrEmpty(error.message))
                return error.message;
        }

        return www.error;
    }
}