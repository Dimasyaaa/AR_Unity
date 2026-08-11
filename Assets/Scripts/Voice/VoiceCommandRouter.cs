using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class VoiceCommandRouter : MonoBehaviour
{
    [System.Serializable]
    public class VoiceAction
    {
        public string[] Keys;   // синонимы: {"синхронизация","синхрон"}
        public UnityEvent Action;
    }

    [Header("Кастомные команды этой сцены (проверяются первыми)")]
    [SerializeField] private List<VoiceAction> customActions = new List<VoiceAction>();

    [Header("Базовые команды")]
    public UnityEvent OnNext;
    public UnityEvent OnBack;
    public UnityEvent OnSelect;
    public UnityEvent OnScan;
    public UnityEvent<string> OnUnknown;

    private void Start() { RealWearVoiceController.Instance.OnCommandRecognized += Handle; }

    private void OnDisable()
    {
        if (RealWearVoiceController.Instance != null)
            RealWearVoiceController.Instance.OnCommandRecognized -= Handle;
    }

    private void Handle(string raw)
    {
        string s = Normalize(raw);
        Debug.Log("[Voice] " + s);

        foreach (var va in customActions)
        {
            if (va.Keys == null) continue;
            foreach (var k in va.Keys)
                if (!string.IsNullOrEmpty(k) && s.Contains(Normalize(k))) { va.Action.Invoke(); return; }
        }

        if (Any(s, "далее", "дальше", "вперед", "следующ")) OnNext.Invoke();
        else if (Any(s, "отмена", "возврат", "назад", "вернись")) OnBack.Invoke();
        else if (Any(s, "выбрать", "подтвердить", "окей", "окэй", "готово")) OnSelect.Invoke();
        else if (Any(s, "скан", "штрих", "куар", "qr")) OnScan.Invoke();
        else OnUnknown?.Invoke(s);
    }

    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.ToLowerInvariant().Replace('ё', 'е');
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s) sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        return sb.ToString().Trim();
    }

    private static bool Any(string s, params string[] keys)
    { foreach (var k in keys) if (s.Contains(k)) return true; return false; }
}