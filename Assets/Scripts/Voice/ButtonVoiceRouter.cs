using System;
using UnityEngine;
using UnityEngine.UI;

public class ButtonVoiceRouter : MonoBehaviour
{
    [Serializable]
    public class PhraseButton
    {
        public string[] phrases;
        public Button button;
    }

    [SerializeField] private PhraseButton[] items;

    void Start()
    {
        if (VoskSpeechProvider.Instance)
            VoskSpeechProvider.Instance.OnFinalResult += OnText;
    }

    void OnDestroy()
    {
        if (VoskSpeechProvider.Instance)
            VoskSpeechProvider.Instance.OnFinalResult -= OnText;
    }

    private void OnText(string raw)
    {
        string t = VoiceText.Normalize(raw);

        foreach (var item in items)
        {
            if (item.button == null || item.phrases == null) continue;
            foreach (var p in item.phrases)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (t.Contains(VoiceText.Normalize(p)))
                {
                    Debug.Log("[Voice] команда: " + p);
                    item.button.onClick.Invoke();
                    return;
                }
            }
        }
    }
}