using System;
using UnityEngine;
using UnityEngine.UI;

public class UiClickBridge : MonoBehaviour
{
    [SerializeField] private Button button;
    string name = string.Empty;
    public void Click()
    {
        if (button != null) button.onClick.Invoke();
    }
}