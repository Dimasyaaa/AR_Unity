using TMPro;
using UnityEngine;

public class VoiceDropdownBinder : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown dropdown;

    private void Start() { RealWearVoiceController.Instance.OnCommandRecognized += TrySelect; }

    private void OnDisable()
    {
        if (RealWearVoiceController.Instance != null)
            RealWearVoiceController.Instance.OnCommandRecognized -= TrySelect;
    }

    private void TrySelect(string raw)
    {
        string s = VoiceCommandRouter.Normalize(raw);
        for (int i = 0; i < dropdown.options.Count; i++)
        {
            string opt = VoiceCommandRouter.Normalize(dropdown.options[i].text);
            if (opt.Length > 2 && (s.Contains(opt) || opt.Contains(s))) { dropdown.value = i; return; }
        }
    }
}