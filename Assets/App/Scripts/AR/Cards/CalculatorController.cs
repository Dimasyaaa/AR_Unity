using System.Globalization;
using UnityEngine;
using ArInventory.Local;

/// <summary>
/// Подключает действия к заранее собранному префабу калькулятора.
/// Отображение, кнопки и их расположение задаются в CalculatorCard.
/// </summary>
public class CalculatorController : MonoBehaviour
{
    private static readonly string[] ButtonLabels =
    {
        "7", "8", "9", "/",
        "4", "5", "6", "*",
        "1", "2", "3", "-",
        "C", "0", "=", "+"
    };

    private TMPro.TextMeshProUGUI displayText;
    private string expression = string.Empty;

    private void Start()
    {
        // Находим готовую разметку в префабе и подключаем к ней обработчики.
        var canvas = GetComponentInChildren<UnityEngine.Canvas>(true);
        if (canvas == null)
        {
            Debug.LogError("[Calc] В префабе калькулятора не найден Canvas.", this);
            return;
        }

        var panel = canvas.transform.Find("Panel");
        if (panel == null)
        {
            Debug.LogError("[Calc] В Canvas не найдена панель Panel. Проверь префаб CalculatorCard.", this);
            return;
        }

        BindUI(panel);
    }

    /// <summary>Подключает кнопки префаба к вычислениям и закрытию карточки.</summary>
    private void BindUI(Transform panel)
    {
        var panelImage = panel.GetComponent<UnityEngine.UI.Image>();
        if (panelImage != null)
            ARCardSpawnUtility.AddPanelDepth(panelImage);

        var display = panel.Find("Display");
        displayText = display != null ? display.GetComponent<TMPro.TextMeshProUGUI>() : null;
        if (displayText == null)
            Debug.LogError("[Calc] В Panel не найден текст Display типа TextMeshProUGUI.", this);

        var grid = panel.Find("Grid");
        if (grid == null)
        {
            Debug.LogError("[Calc] В Panel не найдена сетка Grid с кнопками.", this);
        }
        else
        {
            foreach (var label in ButtonLabels)
            {
                // Ищем непосредственного потомка: слэш в имени Btn_/ является частью имени.
                var buttonObject = FindDirectChild(grid, "Btn_" + label);
                var button = buttonObject != null
                    ? buttonObject.GetComponent<UnityEngine.UI.Button>()
                    : null;

                if (button == null)
                {
                    Debug.LogWarning($"[Calc] В Grid не найдена кнопка Btn_{label}.", this);
                    continue;
                }

                var buttonLabel = label;
                button.onClick.AddListener(() => OnButton(buttonLabel));
            }
        }

        var close = panel.Find("Close");
        var closeButton = close != null ? close.GetComponent<UnityEngine.UI.Button>() : null;
        if (closeButton == null)
            Debug.LogWarning("[Calc] В Panel не найдена кнопка Close.", this);
        else
            closeButton.onClick.AddListener(() => ARCardModeSelector.CloseCard(gameObject));
    }

    /// <summary>Обрабатывает нажатие экранной кнопки калькулятора.</summary>
    private void OnButton(string label)
    {
        if (label == "C")
        {
            expression = string.Empty;
            SetDisplay("0");
            return;
        }

        if (label == "=")
        {
            OnEquals();
            return;
        }

        expression += label;
        SetDisplay(expression);
    }

    /// <summary>Передаёт распознанную голосовую команду тому же обработчику кнопок.</summary>
    public void PressFromVoice(string label)
    {
        if (label == "×") label = "*";
        if (label == "÷") label = "/";
        if (label == "очистить") label = "C";
        if (label == "равно") label = "=";
        if (label == "умножить") label = "*";
        if (label == "разделить") label = "/";

        OnButton(label);
    }

    /// <summary>Проверяет и вычисляет пример, распознанный целиком голосом.</summary>
    public bool EvaluateVoiceExpression(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string normalized = value.Replace(',', '.').Replace(" ", string.Empty);
        foreach (char character in normalized)
        {
            if (!char.IsDigit(character) && character != '.' && character != '+' &&
                character != '-' && character != '*' && character != '/')
                return false;
        }

        expression = normalized;
        OnEquals();
        return true;
    }

    /// <summary>Вычисляет последнюю введённую бинарную операцию и пишет её в журнал.</summary>
    private void OnEquals()
    {
        char operation = '\0';
        int operationIndex = -1;

        // Последний знак после первого символа считается операцией между числами.
        for (int index = expression.Length - 1; index > 0; index--)
        {
            char character = expression[index];
            if (character == '+' || character == '-' || character == '*' || character == '/')
            {
                operation = character;
                operationIndex = index;
                break;
            }
        }

        if (operationIndex < 0)
        {
            SetDisplay("Введите пример");
            return;
        }

        string left = expression.Substring(0, operationIndex);
        string right = expression.Substring(operationIndex + 1);
        var culture = CultureInfo.InvariantCulture;
        if (!double.TryParse(left, NumberStyles.Any, culture, out double firstNumber) ||
            !double.TryParse(right, NumberStyles.Any, culture, out double secondNumber))
        {
            SetDisplay("Ошибка ввода");
            return;
        }

        double resultValue;
        switch (operation)
        {
            case '+': resultValue = firstNumber + secondNumber; break;
            case '-': resultValue = firstNumber - secondNumber; break;
            case '*': resultValue = firstNumber * secondNumber; break;
            default:
                resultValue = secondNumber == 0 ? double.NaN : firstNumber / secondNumber;
                break;
        }

        string result = double.IsNaN(resultValue)
            ? "деление на 0"
            : resultValue.ToString("0.####", culture);

        string calculationExpression = expression;
        SetDisplay($"{calculationExpression} = {result}");

        // Запись в журнал выполняется через локальный клиент; вычисление от сервера не зависит.
        var localClient = LocalClient.Instance;
        if (localClient != null)
        {
            localClient.LogCalculator(
                calculationExpression,
                result,
                onSuccess: () => Debug.Log($"[Calc] Записано: {calculationExpression} = {result}"),
                onError: error => Debug.LogError("[Calc] " + error));
        }
        else
        {
            Debug.LogWarning("[Calc] Результат рассчитан, но LocalClient отсутствует — запись в журнал пропущена.");
        }

        expression = string.Empty;
    }

    /// <summary>Обновляет показание, если текстовый элемент присутствует в префабе.</summary>
    private void SetDisplay(string text)
    {
        if (displayText != null)
            displayText.text = text;
    }

    /// <summary>Ищет объект по имени только среди непосредственных дочерних элементов.</summary>
    private static Transform FindDirectChild(Transform parent, string childName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == childName)
                return child;
        }

        return null;
    }
}
