using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Запускает Android SpeechRecognizer и передаёт распознанные фразы в Unity.
/// Ответы Android складываются в очередь и обрабатываются в Update на главном потоке.
/// </summary>
public class VoiceController : MonoBehaviour
{
    /// <summary>Единственный активный компонент распознавания речи.</summary>
    public static VoiceController Instance { get; private set; }

    /// <summary>Событие для VoiceRouter: распознанная фраза уже на потоке Unity.</summary>
    public event System.Action<string> OnHeard;

    // Android-объекты распознавателя и запроса на распознавание речи.
    private AndroidJavaObject recognizer;
    private AndroidJavaObject intent;

    // Очередь разделяет поток Android, который присылает распознавание,
    // и главный поток Unity, который обрабатывает пользовательский интерфейс.
    private readonly Queue<string> pendingTexts = new Queue<string>();
    private readonly object queueLock = new object();
    // Количество кадров до повторного запуска распознавания; -1 означает, что перезапуск не запланирован.
    private int restartInFrames = -1;
    // Флаг читается из Unity и Android-потоков, поэтому объявлен volatile.
    private volatile bool microphoneReserved;

    /// <summary>Оставляет в приложении только один постоянный экземпляр компонента.</summary>
    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>На Android запрашивает доступ к микрофону и запускает распознавание.</summary>
    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        RequestMicAndInit();
#else
        Debug.Log("[Voice] Editor: голос отключён, тестируй на Android");
#endif
    }

    /// <summary>Передаёт результаты Android подписчикам на главном потоке и перезапускает слушатель.</summary>
    void Update()
    {
        // Передаём накопившиеся фразы подписчикам на главном потоке Unity.
        while (true)
        {
            string text = null;
            lock (queueLock)
            {
                if (pendingTexts.Count > 0) text = pendingTexts.Dequeue();
            }
            if (text == null) break;
            if (!string.IsNullOrEmpty(text))
                OnHeard?.Invoke(text);
        }

        // После завершения или ошибки запускаем распознавание снова с небольшой паузой.
        bool restartNow = false;
        lock (queueLock)
        {
            if (restartInFrames > 0)
                restartInFrames--;
            else if (restartInFrames == 0)
            {
                restartInFrames = -1;
                restartNow = true;
            }
        }
        if (restartNow) RunOnUI(StartListening);
    }

    /// <summary>Запрашивает разрешение Android, если оно ещё не выдано.</summary>
    private void RequestMicAndInit()
    {
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
        {
            var cb = new UnityEngine.Android.PermissionCallbacks();
            cb.PermissionGranted += _ => RunOnUI(InitAndStart);
            cb.PermissionDenied += _ => Debug.LogError("[Voice] Микрофон запрещён");
            UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, cb);
        }
        else RunOnUI(InitAndStart);
    }

    /// <summary>Выполняет Android-вызов в UI-потоке приложения.</summary>
    private void RunOnUI(System.Action action)
    {
        using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
            act.Call("runOnUiThread", new RunnableProxy(action));
    }

    /// <summary>Адаптер делегата C# к интерфейсу Runnable из Android.</summary>
    private class RunnableProxy : AndroidJavaProxy
    {
        private System.Action a;
        public RunnableProxy(System.Action a) : base("java.lang.Runnable") { this.a = a; }
        public void run() { a(); }
    }

    /// <summary>Создаёт Android SpeechRecognizer, настраивает русский язык и начинает слушать.</summary>
    private void InitAndStart()
    {
        try
        {
            using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                var srClass = new AndroidJavaClass("android.speech.SpeechRecognizer");
                recognizer = srClass.CallStatic<AndroidJavaObject>("createSpeechRecognizer", act);
                recognizer.Call("setRecognitionListener", new ListenerProxy(this));

                intent = new AndroidJavaObject("android.content.Intent", "android.speech.action.RECOGNIZE_SPEECH");
                intent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.LANGUAGE_MODEL", "free_form");
                intent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.LANGUAGE", "ru-RU");
                intent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.PARTIAL_RESULTS", true);
                intent.Call<AndroidJavaObject>("putExtra", "android.speech.extra.MAX_RESULTS", 1);

                StartListening();
            }
        }
        catch (System.Exception e) { Debug.LogError("[Voice] Init failed: " + e.Message); }
    }

    /// <summary>Запускает очередной цикл распознавания, если микрофон не занят другой функцией.</summary>
    public void StartListening()
    {
        if (!microphoneReserved && recognizer != null && intent != null)
            recognizer.Call("startListening", intent);
    }

    /// <summary>Временно останавливает распознавание для записи отчёта и затем возобновляет его.</summary>
    public void ReserveMicrophone(bool reserved)
    {
        if (microphoneReserved == reserved) return;
        lock (queueLock)
        {
            microphoneReserved = reserved;
            pendingTexts.Clear();
            restartInFrames = reserved ? -1 : 20;
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        if (reserved)
            RunOnUI(() => recognizer?.Call("cancel"));
#endif
    }

    // Методы ниже вызываются из Android-прослойки: они только сохраняют результат,
    // а Unity-интерфейс обновляется позже в Update на главном потоке.
    /// <summary>Сохраняет распознанную фразу и планирует следующий цикл прослушивания.</summary>
    public void HandleResult(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            lock (queueLock)
            {
                if (microphoneReserved) return;
                pendingTexts.Enqueue(text);
                restartInFrames = 20; // пауза ~0.3 c перед следующим прослушиванием
            }
        }
        else
        {
            lock (queueLock)
                if (!microphoneReserved) restartInFrames = 20;
        }
    }

    /// <summary>Запоминает ошибку Android и планирует повторную попытку.</summary>
    public void HandleError(int code)
    {
        lock (queueLock)
        {
            if (microphoneReserved) return;
            Debug.LogWarning($"[Voice] Ошибка распознавателя {code}, перезапуск");
            restartInFrames = 30;
        }
    }

    /// <summary>Извлекает первую распознанную фразу из ответа Android.</summary>
    public void ExtractAndHandle(AndroidJavaObject bundle)
    {
        string text = null;
        try
        {
            var list = bundle.Call<AndroidJavaObject>("getStringArrayList", "results_recognition");
            if (list != null) text = list.Call<string>("get", 0);
        }
        catch (System.Exception e) { Debug.LogWarning("[Voice] extract: " + e.Message); }
        HandleResult(text);
    }

    /// <summary>Принимает обязательные события Android SpeechRecognizer и передаёт результаты компоненту.</summary>
    private class ListenerProxy : AndroidJavaProxy
    {
        private VoiceController o;
        public ListenerProxy(VoiceController o) : base("android.speech.RecognitionListener") { this.o = o; }

        public void onReadyForSpeech(AndroidJavaObject p) { }
        public void onBeginningOfSpeech() { }
        public void onRmsChanged(float v) { }
        public void onBufferReceived(byte[] b) { }
        public void onEndOfSpeech() { }
        public void onError(int code) { o.HandleError(code); }
        public void onResults(AndroidJavaObject r) { o.ExtractAndHandle(r); }
        // Остальные события интерфейса Android здесь не нужны приложению.
        public void onPartialResults(AndroidJavaObject r) { }
        public void onEvent(int t, AndroidJavaObject p) { }
    }
}
