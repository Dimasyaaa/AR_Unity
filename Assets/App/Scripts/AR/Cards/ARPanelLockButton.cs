using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>Button behavior for pinning an AR card in its current position.</summary>
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
