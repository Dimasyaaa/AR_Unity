using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Вешается на кнопку "Button (Image)". Ставит индекс карточки и включает тап-спавн.
[RequireComponent(typeof(Button))]
public class ImageButtonHook : MonoBehaviour
{
    [SerializeField] private int imageCardIndex = 8; // индекс ImageCardVariant в ObjectSpawner

    private UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets.ObjectSpawner objectSpawner;
    private Behaviour spawnTrigger;

    void Start()
    {
        var button = GetComponent<Button>();

        objectSpawner = FindObjectOfType<UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets.ObjectSpawner>();
        if (objectSpawner != null)
            foreach (var c in objectSpawner.GetComponents<Component>())
                if (c.GetType().Name == "ARInteractorSpawnTrigger")
                    spawnTrigger = c as Behaviour;

        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(OnPressed);
    }

    private void OnPressed()
    {
        if (FindObjectOfType<ImageCardController>() != null)
        {
            Debug.Log("[ImageHook] Карточка уже на сцене");
            return;
        }

        if (objectSpawner == null) { Debug.LogError("[ImageHook] ObjectSpawner не найден"); return; }

        objectSpawner.SetSpawnObjectIndex(imageCardIndex);
        if (spawnTrigger != null) spawnTrigger.enabled = true;

        Debug.Log("[ImageHook] Тапни по поверхности, чтобы поставить карточку");
    }
}