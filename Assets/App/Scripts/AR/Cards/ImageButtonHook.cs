using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Вешается на кнопку карточки продукции. Выбирает префаб напрямую, без ручного индекса.
[RequireComponent(typeof(Button))]
public class ImageButtonHook : MonoBehaviour
{
    [SerializeField] private GameObject cardPrefab;

    public void Configure(GameObject prefab) => cardPrefab = prefab;

    void Start()
    {
        var button = GetComponent<Button>();

        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
        {
            if (button.onClick.GetPersistentMethodName(i) == "SetObjectToSpawn")
                button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
        }

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(OnPressed);
    }

    private void OnPressed()
    {
        ARCardModeSelector.Show(cardPrefab);
    }

    public void SpawnCard() => OnPressed();
}
