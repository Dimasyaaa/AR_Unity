using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Отображает одну строку чек-листа и передаёт выбранный ответ контроллеру карточки.
/// Ссылки на текст, кнопки и их фоны задаются в шаблоне ItemTemplate префаба.
/// </summary>
public sealed class InventoryChecklistItemRow : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI itemName;
    [SerializeField] private TMP_InputField actualQuantityInput;
    [SerializeField] private Button presentButton;
    [SerializeField] private Button missingButton;
    [SerializeField] private Image presentBackground;
    [SerializeField] private Image missingBackground;

    // ID хранится у строки, чтобы нажатие изменило именно нужный предмет шкафа.
    private long itemId;
    private Action<long, string> statusChanged;
    private Action<long, int> quantityChanged;

    /// <summary>Заполняет строку данными и связывает её кнопки с выбранным предметом.</summary>
    public void Configure(long id, string name, int expectedQuantity,
        Action<long, string> onStatusChanged, Action<long, int> onQuantityChanged)
    {
        itemId = id;
        statusChanged = onStatusChanged;
        quantityChanged = onQuantityChanged;

        if (itemName != null)
            itemName.text = $"{name} · учёт: {expectedQuantity}";

        if (actualQuantityInput != null)
        {
            actualQuantityInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            actualQuantityInput.onEndEdit.RemoveAllListeners();
            actualQuantityInput.onEndEdit.AddListener(value =>
            {
                if (int.TryParse(value, out int quantity))
                    quantityChanged?.Invoke(itemId, Mathf.Clamp(quantity, 0, 100000));
            });
        }

        if (presentButton != null)
        {
            presentButton.onClick.RemoveAllListeners();
            presentButton.onClick.AddListener(() => statusChanged?.Invoke(itemId, "present"));
        }

        if (missingButton != null)
        {
            missingButton.onClick.RemoveAllListeners();
            missingButton.onClick.AddListener(() => statusChanged?.Invoke(itemId, "missing"));
        }

        ShowStatus(string.Empty, 0, expectedQuantity);
    }

    /// <summary>Подсвечивает выбранный ответ, оставляя обе кнопки видимыми.</summary>
    public void ShowStatus(string status, int actualQuantity, int expectedQuantity)
    {
        if (actualQuantityInput != null)
            actualQuantityInput.SetTextWithoutNotify(
                string.IsNullOrEmpty(status) ? string.Empty : actualQuantity.ToString());

        if (presentBackground != null)
            presentBackground.color = status == "present"
                ? new Color(.12f, .85f, .32f)
                : new Color(.05f, .52f, .28f);

        if (missingBackground != null)
            missingBackground.color = status == "missing"
                ? new Color(.95f, .22f, .20f)
                : new Color(.65f, .18f, .18f);
    }
}
