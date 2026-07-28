using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class CubeWebOpener : MonoBehaviour
{
    [Header("Settings")]
    public string url = "https://github.com";

    [Header("Optional")]
    public GameObject visualObject; // Визуал куба (можно скрыть)

    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grabInteractable;

    void Start()
    {
        // Получаем компонент взаимодействия
        grabInteractable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();

        if (grabInteractable == null)
        {
            // Если нет на этом объекте, ищем у детей
            grabInteractable = GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        }

        if (grabInteractable != null)
        {
            // Подписываемся на событие выбора
            grabInteractable.selectEntered.AddListener(OnCubeSelected);
        }
    }

    void OnCubeSelected(SelectEnterEventArgs args)
    {
        Debug.Log("Cube clicked! Opening: " + url);

        // Открываем браузер
        Application.OpenURL(url);

        // Опционально: можно скрыть куб
        // if (visualObject != null) visualObject.SetActive(false);
    }

    void OnDestroy()
    {
        // Отписываемся чтобы не было ошибок
        if (grabInteractable != null)
        {
            grabInteractable.selectEntered.RemoveListener(OnCubeSelected);
        }
    }
}