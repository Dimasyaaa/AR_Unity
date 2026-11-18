using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>Связывает кнопку меню с префабом AR-карточки.</summary>
[RequireComponent(typeof(Button))]
public class ARCardButtonHook : MonoBehaviour
{
    /// <summary>Префаб, который будет выбран нажатием или голосовой командой.</summary>
    [SerializeField] private GameObject cardPrefab;

    private Button button;

    private void Awake()
    {
        button = GetComponent<Button>();

        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
        {
            // Отключаем старый выбор по номеру, оставляя другие события кнопки,
            // например показ маркера выбранного пункта.
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

    /// <summary>Выбирает эту карточку по голосовой команде.</summary>
    public void SelectFromVoice() => OnPressed();
}
