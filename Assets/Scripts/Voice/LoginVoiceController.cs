using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LoginVoiceController : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown serverDropdown;
    [SerializeField] private TMP_InputField fioField;
    [SerializeField] private TMP_InputField passwordField;
    [SerializeField] private Button loginButton;
    [SerializeField] private TextMeshProUGUI hint;

    private TMP_InputField activeField;

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

        // режим диктовки в активное поле
        if (activeField != null)
        {
            if (VoiceText.Has(t, "готово")) { activeField = null; SetHint("Ввод завершён"); return; }
            if (VoiceText.Has(t, "очистить")) { activeField.text = ""; return; }
            if (VoiceText.Has(t, "стереть"))
            {
                if (activeField.text.Length > 0)
                    activeField.text = activeField.text.Substring(0, activeField.text.Length - 1);
                return;
            }
            activeField.text = activeField.text.Length > 0 ? activeField.text + " " + raw : raw;
            return;
        }

        if (VoiceText.Has(t, "фио", "логин"))
        {
            activeField = fioField;
            SetHint("Слушаю ФИО. Скажите «готово»");
        }
        else if (VoiceText.Has(t, "пароль"))
        {
            activeField = passwordField;
            SetHint("Слушаю пароль. Скажите «готово»");
        }
        else if (VoiceText.Has(t, "сервер", "отдел", "опция"))
        {
            serverDropdown.value = (serverDropdown.value + 1) % serverDropdown.options.Count;
        }
        else if (VoiceText.Has(t, "войти", "вход"))
        {
            loginButton.onClick.Invoke();
        }
    }

    private void SetHint(string s) { if (hint) hint.text = s; }
}