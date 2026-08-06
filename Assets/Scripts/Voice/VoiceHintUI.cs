using System;
using UnityEngine;
using TMPro;

public class VoiceHintUI : MonoBehaviour
{
    private TextMeshProUGUI label;
    private Action<string> f, p;

    void Start()
    {
        label = GetComponent<TextMeshProUGUI>();
        var v = VoskSpeechProvider.Instance;
        if (v == null) return;
        f = t => label.text = "Вы сказали: " + t;
        p = t => label.text = "… " + t;
        v.OnFinalResult += f;
        v.OnPartialResult += p;
    }

    void OnDestroy()
    {
        var v = VoskSpeechProvider.Instance;
        if (v == null) return;
        v.OnFinalResult -= f;
        v.OnPartialResult -= p;
    }
}