using TMPro;
using UnityEngine;

public class VoiceDebugOverlay : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI statusText;

    private void Start()
    {
        var v = RealWearVoiceController.Instance;
        v.OnListeningChanged += b => { if (statusText) statusText.text = b ? "Слушаю..." : "Пауза"; };
        v.OnPartialRecognized += s => { if (statusText) statusText.text = "Слышу: " + s; };
        v.OnCommandRecognized += s => { if (statusText) statusText.text = "Команда: " + s; };
        v.OnRecognizerError += (c, d) => { if (statusText) statusText.text = "Ош. " + c + " " + d; };
        v.OnFatalError += m => { if (statusText) statusText.text = m; };
    }
}