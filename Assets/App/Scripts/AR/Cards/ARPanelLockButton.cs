using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Закрепляет AR-карточку на месте и повторно разрешает её перемещение по нажатию.
/// Компонент ставится на заранее созданную в префабе кнопку.
/// </summary>
public sealed class ARPanelLockButton : MonoBehaviour
{
    private XRGrabInteractable grab;
    private TextMeshProUGUI label;
    private bool locked;

    private void Awake()
    {
        grab = GetComponentInParent<XRGrabInteractable>();
        label = GetComponentInChildren<TextMeshProUGUI>(true);
        GetComponent<Button>()?.onClick.AddListener(ToggleLock);
        RefreshLabel();
    }

    public void ToggleLock()
    {
        // При закреплении отключаем захват карточки, но оставляем кнопку доступной.
        if (grab == null) grab = GetComponentInParent<XRGrabInteractable>();
        locked = !locked;
        if (grab != null) grab.enabled = !locked;
        RefreshLabel();
    }

    private void RefreshLabel()
    {
        if (label != null) label.text = locked ? "Переместить" : "Закрепить";
    }
}
