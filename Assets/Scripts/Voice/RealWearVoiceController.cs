using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Android;

public class RealWearVoiceController : MonoBehaviour
{
    public static RealWearVoiceController Instance { get; private set; }

    [Header("Настройки")]
    [SerializeField] private string language = "ru-RU";
    [SerializeField] private bool continuous = true;
    [SerializeField] private float restartDelay = 0.5f;
    [SerializeField] private float watchdogTimeout = 10f;
    [SerializeField] private bool preferOffline = false;

    public event Action<string> OnCommandRecognized;
    public event Action<string> OnPartialRecognized;
    public event Action<bool> OnListeningChanged;
    public event Action<int, string> OnRecognizerError;
    public event Action<string> OnFatalError;

    private readonly Queue<Action> unityQueue = new Queue<Action>();
    private readonly object queueLock = new object();

    private AndroidJavaObject recognizer;
    private AndroidJavaObject intent;
    private RecognitionListenerProxy listenerProxy;

    private bool wantsListening;
    private bool isListening;
    private bool fatal;
    private bool appPaused;
    private int failCount;
    private float nextRestartAt = -1f;
    private float lastVoiceActivity;

    public bool IsListening => isListening;

    private static void Log(string m) { Debug.Log("[RWVoice] " + m); }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    public void StartListening()
    {
        if (fatal) return;
        if (!Permission.HasUserAuthorizedPermission("android.permission.RECORD_AUDIO"))
        {
            Permission.RequestUserPermission("android.permission.RECORD_AUDIO");
            StartCoroutine(WaitPermissionAndStart());
            return;
        }
        wantsListening = true;
        PostToAndroidUiThread(StartInternal);
    }

    public void StopListening()
    {
        wantsListening = false;
        nextRestartAt = -1f;
        PostToAndroidUiThread(StopInternal);
        EnqueueUnity(() => SetListening(false));
    }

    public void OnRealWearSystemCommand(string cmd)
    {
        if (!string.IsNullOrEmpty(cmd)) OnCommandRecognized?.Invoke(cmd);
    }

    private void OnApplicationPause(bool paused)
    {
        Log("OnApplicationPause=" + paused);
        appPaused = paused;
        if (paused)
        {
            PostToAndroidUiThread(PauseInternal);
            EnqueueUnity(() => SetListening(false));
        }
        else if (wantsListening)
        {
            EnqueueUnity(() => ScheduleRestart(0.5f));
        }
    }

    private IEnumerator WaitPermissionAndStart()
    {
        float t = 0f;
        while (!Permission.HasUserAuthorizedPermission("android.permission.RECORD_AUDIO") && t < 15f)
        { t += Time.deltaTime; yield return null; }

        if (!Permission.HasUserAuthorizedPermission("android.permission.RECORD_AUDIO"))
        {
            EnqueueUnity(() => OnFatalError?.Invoke("Нет разрешения RECORD_AUDIO."));
            yield break;
        }

        yield return new WaitForSecondsRealtime(1.5f); // активности вернуться из паузы после диалога
        wantsListening = true;
        PostToAndroidUiThread(StartInternal);
    }

    private void Update()
    {
        while (true)
        {
            Action a;
            lock (queueLock) { a = unityQueue.Count > 0 ? unityQueue.Dequeue() : null; }
            if (a == null) break;
            try { a(); } catch (Exception e) { Debug.LogException(e); }
        }

        if (nextRestartAt > 0f && Time.unscaledTime >= nextRestartAt)
        {
            nextRestartAt = -1f;
            PostToAndroidUiThread(StartInternal);
        }

        if (isListening && !appPaused && watchdogTimeout > 0f &&
            Time.unscaledTime - lastVoiceActivity > watchdogTimeout)
        {
            Log("Watchdog: пересоздаём распознаватель");
            PostToAndroidUiThread(PauseInternal);
            SetListening(false);
            ScheduleRestart(0.3f);
        }
    }

    private void OnApplicationQuit()
    {
        wantsListening = false;
        try { PostToAndroidUiThread(StopInternal); } catch { }
    }

    private void StartInternal()
    {
        try
        {
            AndroidJavaObject activity;
            using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                activity = up.GetStatic<AndroidJavaObject>("currentActivity");

            using (var sr = new AndroidJavaClass("android.speech.SpeechRecognizer"))
            {
                bool available = sr.CallStatic<bool>("isRecognitionAvailable", activity);
                Log("StartInternal: available=" + available +
                    " preferOffline=" + preferOffline +
                    " service=" + ResolveServicePackage(activity));
                if (!available)
                {
                    EnqueueUnity(() =>
                    {
                        fatal = true;
                        OnFatalError?.Invoke("На устройстве нет RecognitionService.");
                    });
                    return;
                }
            }

            DestroyRecognizer();
            if (intent != null) { try { intent.Dispose(); } catch { } intent = null; }

            listenerProxy = new RecognitionListenerProxy(this);
            using (var sr = new AndroidJavaClass("android.speech.SpeechRecognizer"))
                recognizer = sr.CallStatic<AndroidJavaObject>("createSpeechRecognizer", activity);
            recognizer.Call("setRecognitionListener", listenerProxy);

            intent = BuildIntent();
            recognizer.Call("startListening", intent);
            Log("startListening вызван");
            EnqueueUnity(() => { lastVoiceActivity = Time.unscaledTime; SetListening(true); });
        }
        catch (Exception e)
        {
            EnqueueUnity(() => OnFatalError?.Invoke("JNI ошибка старта: " + e.Message));
        }
    }

    private static string ResolveServicePackage(AndroidJavaObject activity)
    {
        try
        {
            using (var rs = new AndroidJavaClass("android.speech.RecognitionService"))
            using (var q = new AndroidJavaObject("android.content.Intent", rs.GetStatic<string>("SERVICE_INTERFACE")))
            using (var pm = activity.Call<AndroidJavaObject>("getPackageManager"))
            using (var res = pm.Call<AndroidJavaObject>("resolveService", q, 0))
            {
                if (res == null) return "NONE";
                using (var si = res.Get<AndroidJavaObject>("serviceInfo"))
                    return si.Get<string>("packageName");
            }
        }
        catch (Exception e) { return "err:" + e.Message; }
    }

    private void StopInternal() { DestroyRecognizer(); }
    private void PauseInternal() { DestroyRecognizer(); }

    private void DestroyRecognizer()
    {
        if (recognizer != null)
        {
            try { recognizer.Call("cancel"); recognizer.Call("destroy"); } catch { }
            recognizer = null;
        }
    }

    private AndroidJavaObject BuildIntent()
    {
        using (var ri = new AndroidJavaClass("android.speech.RecognizerIntent"))
        {
            var i = new AndroidJavaObject("android.content.Intent",
                ri.GetStatic<string>("ACTION_RECOGNIZE_SPEECH"));

            i.Call<AndroidJavaObject>("putExtra",
                ri.GetStatic<string>("EXTRA_LANGUAGE_MODEL"),
                ri.GetStatic<string>("LANGUAGE_MODEL_FREE_FORM"));
            i.Call<AndroidJavaObject>("putExtra", ri.GetStatic<string>("EXTRA_LANGUAGE"), language);
            i.Call<AndroidJavaObject>("putExtra", ri.GetStatic<string>("EXTRA_PARTIAL_RESULTS"), true);
            i.Call<AndroidJavaObject>("putExtra", ri.GetStatic<string>("EXTRA_MAX_RESULTS"), 1);
            i.Call<AndroidJavaObject>("putExtra", ri.GetStatic<string>("EXTRA_PREFER_OFFLINE"), preferOffline);
            i.Call<AndroidJavaObject>("putExtra",
                ri.GetStatic<string>("EXTRA_SPEECH_INPUT_COMPLETE_SILENCE_LENGTH_MILLIS"), 700L);
            return i;
        }
    }

    private void SetListening(bool v)
    {
        if (isListening == v) return;
        isListening = v;
        OnListeningChanged?.Invoke(v);
    }

    internal void ScheduleRestart(float baseDelay)
    {
        if (!wantsListening || !continuous || fatal || appPaused) return;
        float delay = Mathf.Min(baseDelay * Mathf.Pow(2f, failCount), 5f);
        failCount++;
        nextRestartAt = Time.unscaledTime + Mathf.Max(delay, 0.2f);
    }

    internal void EnqueueUnity(Action a) { lock (queueLock) unityQueue.Enqueue(a); }

    internal void PostToAndroidUiThread(Action a)
    {
        try
        {
            using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = up.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                activity.Call("runOnUiThread", new RunnableProxy(a));
            }
        }
        catch (Exception e) { Log("PostToAndroidUiThread: " + e.Message); }
    }

    public static string DescribeError(int code)
    {
        switch (code)
        {
            case 1: return "NETWORK_TIMEOUT";
            case 2: return "CLIENT";
            case 3: return "SERVER";
            case 4: return "AUDIO";
            case 5: return "NO_MATCH";
            case 6: return "BUSY";
            case 7: return "PERMISSIONS";
            case 8: return "SPEECH_TIMEOUT";
            default: return "ERR_" + code;
        }
    }

    private sealed class RunnableProxy : AndroidJavaProxy
    {
        private readonly Action action;
        public RunnableProxy(Action a) : base("java.lang.Runnable") { action = a; }
        public void run() { action(); }
    }

    private sealed class RecognitionListenerProxy : AndroidJavaProxy
    {
        private readonly RealWearVoiceController c;
        public RecognitionListenerProxy(RealWearVoiceController controller)
            : base("android.speech.RecognitionListener") { c = controller; }

        public void onReadyForSpeech(AndroidJavaObject p)
        {
            Log(">>> onReadyForSpeech (микрофон открыт)");
            c.EnqueueUnity(() => { c.failCount = 0; c.lastVoiceActivity = Time.unscaledTime; });
        }

        public void onBeginningOfSpeech()
        {
            Log(">>> onBeginningOfSpeech (движок СЛЫШИТ звук)");
            c.EnqueueUnity(() => { c.failCount = 0; c.lastVoiceActivity = Time.unscaledTime; });
        }

        public void onRmsChanged(float rmsdB) { }
        public void onBufferReceived(byte[] buffer) { }

        public void onEndOfSpeech()
        {
            Log(">>> onEndOfSpeech");
            c.EnqueueUnity(() =>
            {
                c.SetListening(false);
                if (c.wantsListening) c.ScheduleRestart(c.restartDelay);
            });
        }

        public void onError(int error)
        {
            Log(">>> onError " + error + " (" + DescribeError(error) + ")");
            c.EnqueueUnity(() =>
            {
                c.SetListening(false);
                c.OnRecognizerError?.Invoke(error, DescribeError(error));
                if (error == 7) { c.fatal = true; c.OnFatalError?.Invoke("Нет разрешения на микрофон."); return; }
                if (c.wantsListening) c.ScheduleRestart(c.restartDelay);
            });
        }

        public void onResults(AndroidJavaObject results)
        {
            string text = Extract(results);
            Log(">>> onResults: '" + text + "'");
            c.EnqueueUnity(() =>
            {
                c.failCount = 0;
                if (!string.IsNullOrEmpty(text)) c.OnCommandRecognized?.Invoke(text);
                c.SetListening(false);
                if (c.wantsListening) c.ScheduleRestart(0.2f);
            });
        }

        public void onPartialResults(AndroidJavaObject results)
        {
            string text = Extract(results);
            Log(">>> onPartialResults: '" + text + "'");
            c.EnqueueUnity(() =>
            {
                c.lastVoiceActivity = Time.unscaledTime;
                if (!string.IsNullOrEmpty(text)) c.OnPartialRecognized?.Invoke(text);
            });
        }

        public void onEvent(int eventType, AndroidJavaObject p) { }

        private static string Extract(AndroidJavaObject bundle)
        {
            if (bundle == null) return null;
            using (var list = bundle.Call<AndroidJavaObject>("getStringArrayList", "results"))
            {
                if (list == null || list.Call<int>("size") == 0) return null;
                return list.Call<string>("get", 0);
            }
        }
    }
}