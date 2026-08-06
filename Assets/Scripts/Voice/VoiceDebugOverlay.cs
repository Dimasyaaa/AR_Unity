using UnityEngine;
using UnityEngine.UI;

public class VoiceDebugOverlay : MonoBehaviour
{
    private Text label;

    void Awake()
    {
        var root = new GameObject("VoiceDebugCanvas");
        root.transform.SetParent(transform, false);

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        root.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("Panel");
        panel.transform.SetParent(root.transform, false);
        var img = panel.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.65f);
        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, 240f);
        rt.anchoredPosition = Vector2.zero;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(panel.transform, false);
        label = textGo.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        label.fontSize = 24;
        label.color = Color.white;
        label.alignment = TextAnchor.UpperLeft;
        var trt = label.rectTransform;
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(15f, 8f);
        trt.offsetMax = new Vector2(-15f, -8f);
    }

    void Update()
    {
        var v = VoskSpeechProvider.Instance;
        if (v == null || label == null) return;

        label.text =
            "ГОЛОС: " + v.Status + "\n" +
            "СЛУШАЮ: " + v.LastPartial + "\n" +
            "КОМАНДА: " + v.LastFinal + "\n" +
            "mic: " + v.MicPos + "\n" +
            "lvl: " + v.Level;
    }
}