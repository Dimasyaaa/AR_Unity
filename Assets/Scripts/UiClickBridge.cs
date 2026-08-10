using UnityEngine;
using UnityEngine.UI;

public class UiClickBridge : MonoBehaviour
{
    [SerializeField] private Button button;

    public void Click()
    {
        if (button != null) button.onClick.Invoke();
    }
}