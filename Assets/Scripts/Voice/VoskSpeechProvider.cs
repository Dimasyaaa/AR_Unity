using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

public class VoskSpeechProvider : MonoBehaviour
{
    public static VoskSpeechProvider Instance;

    public event Action<string> OnFinalResult;
    public event Action<string> OnPartialResult;

    [SerializeField] private string zipName = "vosk_model.zip";
    [SerializeField, Range(1f, 32f)] private float gain = 4f;

    [HideInInspector] public string Status = "старт...";
    [HideInInspector] public string LastPartial = "";
    [HideInInspector] public string LastFinal = "";
    [HideInInspector] public int MicPos = -1;
    [HideInInspector] public int Level = 0;

    private static readonly int[] Sources = { 6, 1, 5, 9, 7, 0 };
    private int sourceIndex = -1;
    private float silenceTime;
    private int readLogs;

    private AndroidJavaObject micRec;
    private byte[] rawBuf = new byte[4096]; // 4096 байт = 2048 сэмплов 16-бит
    private short[] abuf = new short[2048];

    private AudioClip unityClip;
    private int lastPos;
    private float[] fbuf;
    private short[] sbuf;

    private IntPtr model, rec;
    private bool ready;
    private bool modelLoaded;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        gameObject.AddComponent<VoiceDebugOverlay>();
    }

    void Start()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            Permission.RequestUserPermission(Permission.Microphone);
#endif
        StartCoroutine(Init());
    }

    IEnumerator Init()
    {
        Status = "поиск модели...";
        string destRoot = Path.Combine(Application.persistentDataPath, "vosk_model");
        string modelPath = FindModelRoot(destRoot);

        if (modelPath == null)
        {
            Status = "распаковка модели...";
            string tmpZip = Path.Combine(Application.persistentDataPath, "vosk_model.zip");
            using (UnityWebRequest www = UnityWebRequest.Get(
                       Path.Combine(Application.streamingAssetsPath, zipName)))
            {
                yield return www.SendWebRequest();
                if (www.result != UnityWebRequest.Result.Success)
                {
                    Status = "ОШИБКА: нет vosk_model.zip";
                    yield break;
                }
                File.WriteAllBytes(tmpZip, www.downloadHandler.data);
            }

            try
            {
                if (Directory.Exists(destRoot)) Directory.Delete(destRoot, true);
                ZipFile.ExtractToDirectory(tmpZip, destRoot);
                File.Delete(tmpZip);
            }
            catch (Exception e)
            {
                Status = "ОШИБКА распаковки";
                Debug.LogError("[Vosk] " + e);
                yield break;
            }

            modelPath = FindModelRoot(destRoot);
            if (modelPath == null) { Status = "ОШИБКА: в zip нет папки am"; yield break; }
        }

        Status = "загрузка модели...";
        try
        {
            model = VoskNative.ModelNew(modelPath);
            rec = VoskNative.RecognizerNew(model, 16000f);
            modelLoaded = true;
        }
        catch (Exception e)
        {
            Status = "ОШИБКА библиотеки vosk";
            Debug.LogError("[Vosk] " + e);
            yield break;
        }

        StartMic();
    }

    void StartMic()
    {
        if (!modelLoaded) return;
        TakeAudioControl();
        sourceIndex = -1;
        if (!TryOpenNext()) OpenUnityMic();
    }

    void TakeAudioControl()
    {
        try
        {
            using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = up.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var am = activity.Call<AndroidJavaObject>("getSystemService", "audio"))
            {
                am.Call("setMode", 2);
                try { am.Call("requestAudioFocus", null, 3, 1); } catch { }
            }
        }
        catch (Exception e) { Debug.Log("[Vosk] audio control: " + e.Message); }
    }

    void ReleaseAudioControl()
    {
        try
        {
            using (var up = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = up.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var am = activity.Call<AndroidJavaObject>("getSystemService", "audio"))
            {
                am.Call("setMode", 0);
            }
        }
        catch { }
    }

    bool TryOpenNext()
    {
        while (sourceIndex + 1 < Sources.Length)
        {
            sourceIndex++;
            if (OpenAndroidMic(Sources[sourceIndex]))
            {
                ready = true;
                silenceTime = 0f;
                readLogs = 0;
                Status = "ГОТОВ, src=" + Sources[sourceIndex];
                Debug.Log("[Vosk] ГОТОВ, источник AudioRecord=" + Sources[sourceIndex]);
                return true;
            }
        }
        return false;
    }

    bool OpenAndroidMic(int source)
    {
        try
        {
            using (var cls = new AndroidJavaClass("android.media.AudioRecord"))
            {
                int min = cls.CallStatic<int>("getMinBufferSize", 16000, 16, 2);
                if (min <= 0) min = 1280;
                micRec = new AndroidJavaObject("android.media.AudioRecord",
                    source, 16000, 16, 2, min * 4);
                int st = micRec.Call<int>("getState");
                if (st != 1)
                {
                    micRec.Call("release");
                    micRec = null;
                    return false;
                }
                micRec.Call("startRecording");
            }
            return true;
        }
        catch (Exception e)
        {
            Debug.Log("[Vosk] src=" + source + " недоступен: " + e.Message);
            micRec = null;
            return false;
        }
    }

    void OpenUnityMic()
    {
        unityClip = Microphone.Start(null, true, 1, 16000);
        if (unityClip == null) { Status = "ОШИБКА: нет микрофона"; return; }
        lastPos = 0;
        ready = true;
        Status = "ГОТОВ, unity-mic";
        Debug.Log("[Vosk] ГОТОВ, unity-mic");
    }

    void ReleaseAndroidMic()
    {
        if (micRec != null)
        {
            try { micRec.Call("stop"); micRec.Call("release"); } catch { }
            micRec = null;
        }
    }

    void StopAllMic()
    {
        ready = false;
        ReleaseAndroidMic();
        if (unityClip != null) { Microphone.End(null); unityClip = null; }
        ReleaseAudioControl();
    }

    void OnApplicationPause(bool pause)
    {
        Debug.Log("[Vosk] pause=" + pause);
        if (pause) { StopAllMic(); Status = "пауза, микрофон освобождён"; }
        else StartMic();
    }

    void OnApplicationQuit() { StopAllMic(); }

    // ---------- чтение звука через byte[] (стабильный путь) ----------

    int FillFromAndroid()
    {
        // read(byte[] buffer, int offsetInBytes, int sizeInBytes, int readMode)
        int n = micRec.Call<int>("read", rawBuf, 0, rawBuf.Length, 1);
        if (readLogs < 5) { readLogs++; Debug.Log("[Vosk] read bytes=" + n); }
        if (n <= 0) return 0;

        int samples = n / 2; // 16-bit PCM: 2 байта = 1 сэмпл
        if (samples > abuf.Length) samples = abuf.Length;

        for (int i = 0; i < samples; i++)
        {
            // little-endian PCM_16BIT
            int lo = rawBuf[i * 2] & 0xFF;
            int hi = rawBuf[i * 2 + 1];
            short v = (short)((hi << 8) | lo);
            if (gain > 1.01f)
                v = (short)Mathf.Clamp(v * gain, -32768f, 32767f);
            abuf[i] = v;
        }

        MicPos += samples;
        return samples;
    }

    int FillFromUnity()
    {
        int pos = Microphone.GetPosition(null);
        MicPos = pos;
        if (pos == lastPos) return 0;
        int len = (pos - lastPos + unityClip.samples) % unityClip.samples;
        if (len <= 0) return 0;
        if (fbuf == null || fbuf.Length < len) fbuf = new float[len];
        unityClip.GetData(fbuf, lastPos);
        lastPos = pos;
        if (sbuf == null || sbuf.Length < len) sbuf = new short[len];
        for (int i = 0; i < len; i++)
            sbuf[i] = (short)(Mathf.Clamp(fbuf[i] * gain, -1f, 1f) * 32767f);
        return len;
    }

    void Update()
    {
        if (!ready) { MicPos = -1; return; }

        short[] data;
        int count;
        if (micRec != null) { count = FillFromAndroid(); data = abuf; }
        else if (unityClip != null) { count = FillFromUnity(); data = sbuf; }
        else return;

        int maxAbs = 0;
        for (int i = 0; i < count; i++)
        {
            int a = data[i] < 0 ? -data[i] : data[i];
            if (a > maxAbs) maxAbs = a;
        }
        if (count > 0) Level = maxAbs;

        if (micRec != null)
        {
            if (maxAbs > 50) silenceTime = 0f;
            else silenceTime += Time.deltaTime;

            if (silenceTime > 6f)
            {
                Debug.Log("[Vosk] тишина на src=" + Sources[sourceIndex] + ", меняю источник");
                ReleaseAndroidMic();
                if (!TryOpenNext()) OpenUnityMic();
                return;
            }
        }

        if (count <= 0 || rec == IntPtr.Zero) return;

        if (VoskNative.AcceptWaveform(rec, data, count) == 1)
        {
            string text = Extract(VoskNative.Result(rec));
            if (!string.IsNullOrWhiteSpace(text))
            {
                LastFinal = text;
                Debug.Log("[Vosk] FINAL: " + text);
                OnFinalResult?.Invoke(text);
            }
        }
        else if (Time.frameCount % 5 == 0)
        {
            string part = Extract(VoskNative.PartialResult(rec));
            if (part != LastPartial)
            {
                LastPartial = part;
                if (!string.IsNullOrWhiteSpace(part)) Debug.Log("[Vosk] partial: " + part);
                OnPartialResult?.Invoke(part);
            }
        }
    }

    private static string Extract(string json)
    {
        if (string.IsNullOrEmpty(json)) return "";
        var m = Regex.Match(json, "\"(?:text|partial)\"\\s*:\\s*\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : "";
    }

    private static string FindModelRoot(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        if (Directory.Exists(Path.Combine(dir, "am"))) return dir;
        foreach (var sub in Directory.GetDirectories(dir))
            if (Directory.Exists(Path.Combine(sub, "am"))) return sub;
        return null;
    }

    void OnDestroy()
    {
        StopAllMic();
        if (rec != IntPtr.Zero) VoskNative.RecognizerFree(rec);
        if (model != IntPtr.Zero) VoskNative.ModelFree(model);
    }
}