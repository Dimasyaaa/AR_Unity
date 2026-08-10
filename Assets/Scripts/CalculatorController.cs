using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ArInventory.Local;

// Вешается на префаб калькулятора.
// При появлении объекта строит интерфейс внутрь дочернего World Space Canvas.
public class CalculatorController : MonoBehaviour
{
    private TextMeshProUGUI displayText;
    private string expression = "";

    private void Start()
    {
        var canvas = GetComponentInChildren<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[Calc] Canvas не найден на объекте!");
            return;
        }
        BuildUI(canvas.transform);
    }

    // ==========================================
    // Построение интерфейса
    // ==========================================
    private void BuildUI(Transform canvasRoot)
    {
        // Панель на весь canvas
        var panel = CreateUIObject("Panel", canvasRoot);
        var panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0.12f, 0.14f, 0.18f, 0.95f);
        Stretch(panel.GetComponent<RectTransform>());

        var vlg = panel.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(20, 20, 20, 20);
        vlg.spacing = 12;
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;

        // Табло
        var displayGo = CreateUIObject("Display", panel.transform);
        displayText = displayGo.AddComponent<TextMeshProUGUI>();
        SetFont(displayText);
        displayText.text = "0";
        displayText.fontSize = 64;
        displayText.alignment = TextAlignmentOptions.Right;
        displayText.color = Color.white;
        displayGo.AddComponent<LayoutElement>().preferredHeight = 120;

        // Сетка кнопок 4x4
        var gridGo = CreateUIObject("Grid", panel.transform);
        var grid = gridGo.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(120, 120);
        grid.spacing = new Vector2(8, 8);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;
        gridGo.AddComponent<LayoutElement>().preferredHeight = 504;

        string[] labels =
        {
            "7", "8", "9", "/",
            "4", "5", "6", "*",
            "1", "2", "3", "-",
            "C", "0", "=", "+"
        };

        foreach (var label in labels)
        {
            Color color = new Color(0.22f, 0.26f, 0.33f);
            if (label == "=") color = new Color(0.10f, 0.55f, 0.90f);
            if (label == "C") color = new Color(0.80f, 0.25f, 0.25f);
            if (label == "+" || label == "-" || label == "*" || label == "/")
                color = new Color(0.35f, 0.40f, 0.50f);

            CreateButton(gridGo.transform, label, color, () => OnButton(label));
        }

        // Кнопка закрытия (уничтожает объект, как удаляется куб)
        var closeGo = CreateUIObject("Close", panel.transform);
        closeGo.AddComponent<LayoutElement>().preferredHeight = 80;
        var closeBtn = closeGo.AddComponent<Button>();
        closeGo.AddComponent<Image>().color = new Color(0.55f, 0.20f, 0.20f);
        CreateTextChild(closeGo.transform, "Закрыть", 44);
        closeBtn.onClick.AddListener(() => Destroy(gameObject));
    }

    // ==========================================
    // Логика кнопок
    // ==========================================
    private void OnButton(string label)
    {
        if (label == "C")
        {
            expression = "";
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

    private void OnEquals()
    {
        char op = '\0';
        int opIndex = -1;
        for (int i = expression.Length - 1; i > 0; i--)
        {
            char c = expression[i];
            if (c == '+' || c == '-' || c == '*' || c == '/')
            {
                op = c;
                opIndex = i;
                break;
            }
        }

        if (opIndex < 0)
        {
            SetDisplay("Введите пример");
            return;
        }

        string left = expression.Substring(0, opIndex);
        string right = expression.Substring(opIndex + 1);

        var ci = CultureInfo.InvariantCulture;
        if (!double.TryParse(left, NumberStyles.Any, ci, out double a) ||
            !double.TryParse(right, NumberStyles.Any, ci, out double b))
        {
            SetDisplay("Ошибка ввода");
            return;
        }

        double res;
        switch (op)
        {
            case '+': res = a + b; break;
            case '-': res = a - b; break;
            case '*': res = a * b; break;
            default: res = b == 0 ? double.NaN : a / b; break;
        }

        string result = double.IsNaN(res)
            ? "деление на 0"
            : res.ToString("0.####", ci);

        SetDisplay($"{expression} = {result}");

        LocalClient.Instance.LogCalculator(
            expression,
            result,
            onSuccess: () => Debug.Log($"[Calc] Записано: {expression} = {result}"),
            onError: err => Debug.LogError("[Calc] " + err));

        expression = "";
    }

    // ==========================================
    // Вспомогательные методы
    // ==========================================
    private void SetDisplay(string text)
    {
        if (displayText != null)
            displayText.text = text;
    }

    private GameObject CreateUIObject(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    private void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private void CreateButton(Transform parent, string label, Color color, UnityEngine.Events.UnityAction onClick)
    {
        var go = CreateUIObject("Btn_" + label, parent);
        go.AddComponent<Image>().color = color;
        var btn = go.AddComponent<Button>();
        CreateTextChild(go.transform, label, 52);
        btn.onClick.AddListener(onClick);
    }

    private TextMeshProUGUI CreateTextChild(Transform parent, string text, float size)
    {
        var textGo = new GameObject("Text");
        textGo.transform.SetParent(parent, false);
        var tmp = textGo.AddComponent<TextMeshProUGUI>();
        SetFont(tmp);
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        Stretch(textGo.GetComponent<RectTransform>());
        return tmp;
    }

    private void SetFont(TextMeshProUGUI tmp)
    {
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;
    }
}