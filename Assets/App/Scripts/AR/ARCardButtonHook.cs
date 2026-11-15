using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>Reusable button behavior: assign a card prefab and it will be registered with the AR spawner automatically.</summary>
[RequireComponent(typeof(Button))]
public class ARCardButtonHook : MonoBehaviour
{
    [SerializeField] private GameObject cardPrefab;

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();

        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
        {
            // Ignore the template's old numeric selection callback if this button
            // was duplicated from a legacy object-menu entry. Preserve UI callbacks
            // such as showing this button's selection marker.
            if (button.onClick.GetPersistentMethodName(i) == "SetObjectToSpawn")
                button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(OnPressed);
    }

    private void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(OnPressed);
    }

    private void OnPressed()
    {
        ARCardModeSelector.Show(cardPrefab);
    }
}
