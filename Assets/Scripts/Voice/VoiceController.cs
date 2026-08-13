using UnityEngine;
using System.Collections.Generic;

// Голосовой ввод через Android SpeechRecognizer.
// колбэки приходят с Java-потока, поэтому весь Unity-код
// выполняется ТОЛЬКО на главном потоке через очередь в Update().
public class VoiceController : MonoBehaviour
{
    public static VoiceController Instance { get; private set; }
    public event System.Action<string> OnHeard;

    private AndroidJavaObject recognizer;
    private AndroidJavaObject intent;

    // Очередь распознанных фраз (пишет Java-поток, читает Unity-поток)
    private readonly Queue<string> pendingTexts = new Queue<string>();
    private readonly object queueLock = new object();
    private int restartInFrames = -1;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        RequestMicAndInit();
#else
        Debug.Log("[Voice] Editor: голос отключён, тестируй на Android");
#endif
    }

    void Update()
    {
        // 1) выдаём фразы подписчикам НА ГЛАВНОМ ПОТОКЕ UNITY
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

        // 2) перезапуск прослушивания с небольшой паузой
        if (restartInFrames > 0)
        {
            restartInFrames--;
        }
        else if (restartInFrames == 0)
        {
            restartInFrames = -1;
            RunOnUI(StartListening);
        }
    }

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

    private void RunOnUI(System.Action action)
    {
        using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var act = up.GetStatic<AndroidJavaObject>("currentActivity"))
            act.Call("runOnUiThread", new RunnableProxy(action));
    }

    private class RunnableProxy : AndroidJavaProxy
    {
        private System.Action a;
        public RunnableProxy(System.Action a) : base("java.lang.Runnable") { this.a = a; }
        public void run() { a(); }
    }

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

    public void StartListening()
    {
        if (recognizer != null && intent != null)
            recognizer.Call("startListening", intent);
    }

    // ===== ВЫЗЫВАЮТСЯ С JAVA-ПОТОКА: только кладём в очередь, НИКАКОГО Unity-UI =====
    public void HandleResult(string text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            Debug.Log($"[Voice] Слышу: {text}");
            lock (queueLock) pendingTexts.Enqueue(text);
        }
        restartInFrames = 20; // пауза ~0.3 c перед следующим прослушиванием
    }

    public void HandleError(int code)
    {
        Debug.LogWarning($"[Voice] Ошибка распознавателя {code}, перезапуск");
        restartInFrames = 30;
    }

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
        public void onPartialResults(AndroidJavaObject r) { }
        public void onEvent(int t, AndroidJavaObject p) { }
    }
}