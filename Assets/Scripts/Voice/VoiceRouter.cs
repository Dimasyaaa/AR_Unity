using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Голосовые команды + красивая экранная подсказка (слышу / действие / ошибка).
public class VoiceRouter : MonoBehaviour
{
    private Canvas hintCanvas;
    private TextMeshProUGUI hintText;
    private string lastHeard = "";
    private string lastAction = "";
    private TMP_Dropdown lastDropdown;

    void Awake()
    {
        if (VoiceController.Instance == null)
            gameObject.AddComponent<VoiceController>();
    }

    void OnEnable()
    {
        if (VoiceController.Instance) VoiceController.Instance.OnHeard += Handle;
        SceneManager.sceneLoaded += OnSceneLoaded;
        CreateHint();
    }

    void OnDisable()
    {
        if (VoiceController.Instance) VoiceController.Instance.OnHeard -= Handle;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        lastDropdown = null;
    }

    private void Handle(string raw)
    {
        string t = raw.ToLowerInvariant();
        lastHeard = t;
        lastAction = "";
        string scene = SceneManager.GetActiveScene().name;
        Debug.Log($"[VoiceRouter] '{t}' в {scene}");

        try
        {
            if (scene == "StartScene") HandleStart(t);
            else if (scene == "LoginScene") HandleLogin(t);
            else HandleSample(t);
        }
        catch (System.Exception e)
        {
            lastAction = "ОШИБКА: " + e.Message;
            Debug.LogError($"[VoiceRouter] Ошибка при команде '{t}': {e}");
        }
    }

    // ================= START SCENE =================
    private void HandleStart(string t)
    {
        if (t.Contains("скан")) TryPress("ScanButton");
        else if (t.Contains("инструк")) TryPress("InstructionsButton");
        else if (t.Contains("синхрон")) TryPress("SyncButton");
        else if (t.Contains("вай") || t.Contains("вифи") || t.Contains("wifi") || t.Contains("интернет")) TryPress("WifiButton");
        else if (t.Contains("usb") || t.Contains("юсб") || t.Contains("усб") || t.Contains("флеш")) TryPress("UsbButton");
        else if (t.Contains("закры")) TryPress("CloseButton");
        else if (t.Contains("выход")) TryPress("ExitButton");
    }

    // ================= LOGIN SCENE =================
    private void HandleLogin(string t)
    {
        if (t.Contains("фио") || t.Contains("имя") || t.Contains("пользоват"))
        {
            FocusInput("FullNameInput");
            return;
        }

        if (t.Contains("пароль"))
        {
            FocusInput("PasswordInput");
            return;
        }

        if (t.Contains("отдел"))
        {
            var dd = FindDropdown("DepartmentDropdown");
            if (dd != null)
            {
                lastDropdown = dd;
                dd.Show();
                lastAction = "→ открыт список отделов";
            }
            else lastAction = "→ НЕ найден дропдаун отделов";
            return;
        }

        if (t.Contains("вверх") || t.Contains("вниз"))
        {
            if (lastDropdown == null) lastDropdown = FindDropdown("DepartmentDropdown");
            if (lastDropdown != null)
            {
                int delta = t.Contains("вниз") ? 1 : -1;
                lastDropdown.value = Mathf.Clamp(lastDropdown.value + delta, 0, lastDropdown.options.Count - 1);
                lastAction = "→ отдел: " + lastDropdown.captionText.text;
            }
            else lastAction = "→ нет активного списка";
            return;
        }

        if (t.Contains("выбра") || t.Contains("подтвер") || t == "ок")
        {
            if (lastDropdown != null) { lastDropdown.Hide(); lastAction = "→ список закрыт"; }
            return;
        }

        if (t.Contains("войти") || t.Contains("вход"))
        {
            if (!TryPress("LoginButton")) TryPress("войти");
            return;
        }
    }

    // ================= SAMPLE SCENE =================
    private void HandleSample(string t)
    {
        if (t.Contains("калькулятор"))
            FindAnyObjectByType<CalculatorButtonHook>()?.SpawnCalculator();
        else if (t.Contains("веб") || t.Contains("сайт"))
            FindAnyObjectByType<WebButtonHook>()?.OpenWeb();
        else if (t.Contains("карточ") || t.Contains("изображ") || t.Contains("фото"))
            FindAnyObjectByType<ImageButtonHook>()?.SpawnCard();
        else if (t.Contains("положи") || t.Contains("закреп"))
            PinAll(true);
        else if (t.Contains("отпусти") || t.Contains("сними"))
            PinAll(false);
        else if (t.Contains("закры"))
            CloseAll();
        else if (t.Contains("назад"))
            SceneManager.LoadScene("StartScene");
    }

    // ==========================================
    // Поиск и нажатие кнопок (с диагностикой)
    // ==========================================
    private bool TryPress(string goName)
    {
        var all = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include);

        foreach (var b in all)
            if (b.gameObject.name == goName)
            { b.onClick.Invoke(); lastAction = "→ нажата " + goName; return true; }

        foreach (var b in all)
            if (b.gameObject.name.IndexOf(goName, StringComparison.OrdinalIgnoreCase) >= 0)
            { b.onClick.Invoke(); lastAction = "→ нажата " + b.gameObject.name; return true; }

        foreach (var b in all)
        {
            var s = GetButtonText(b);
            if (!string.IsNullOrEmpty(s) && s.ToLowerInvariant().Contains(goName.ToLowerInvariant()))
            { b.onClick.Invoke(); lastAction = "→ нажата " + b.gameObject.name; return true; }
        }

        lastAction = "→ НЕ найдена кнопка '" + goName + "'";
        return false;
    }

    private void FocusInput(string goName)
    {
        var all = UnityEngine.Object.FindObjectsByType<TMP_InputField>(FindObjectsInactive.Include);
        foreach (var i in all)
            if (i.gameObject.name == goName)
            {
                i.Select();
                i.ActivateInputField();
                lastAction = "→ фокус на " + goName;
                return;
            }
        lastAction = "→ НЕ найдено поле '" + goName + "'";
    }

    private TMP_Dropdown FindDropdown(string goName)
    {
        var all = UnityEngine.Object.FindObjectsByType<TMP_Dropdown>(FindObjectsInactive.Include);
        foreach (var d in all)
            if (d.gameObject.name == goName) return d;
        return null;
    }

    private string GetButtonText(Button b)
    {
        var tmp = b.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null) return tmp.text;
        var legacy = b.GetComponentInChildren<UnityEngine.UI.Text>(true);
        if (legacy != null) return legacy.text;
        return b.gameObject.name;
    }

    private void PinAll(bool pin)
    {
        var calc = FindAnyObjectByType<CalculatorController>();
        if (calc != null) calc.IsPinned = pin;

        var web = FindAnyObjectByType<WebCardController>();
        if (web != null) web.IsPinned = pin;

        lastAction = pin ? "→ закреплено" : "→ отпущено";
    }

    private void CloseAll()
    {
        if (WebOverlayController.Instance) WebOverlayController.Instance.Close();
        var calc = FindAnyObjectByType<CalculatorController>(); if (calc) Destroy(calc.gameObject);
        var web = FindAnyObjectByType<WebCardController>(); if (web) Destroy(web.gameObject);
        var img = FindAnyObjectByType<ImageCardController>(); if (img) Destroy(img.gameObject);
        lastAction = "→ закрыто";
    }

    // ==========================================
    // Красивая подсказка: панель + цветной текст
    // ==========================================
    private void CreateHint()
    {
        if (hintCanvas != null) return;

        var go = new GameObject("VoiceHint");
        go.transform.SetParent(transform);
        hintCanvas = go.AddComponent<Canvas>();
        hintCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        hintCanvas.sortingOrder = 9500;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        // Полупрозрачная панель снизу
        var panel = new GameObject("Panel");
        panel.transform.SetParent(go.transform, false);
        var img = panel.AddComponent<Image>();
        img.color = new Color(0.04f, 0.07f, 0.12f, 0.72f);
        img.raycastTarget = false;
        var prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(0, 0);
        prt.anchorMax = new Vector2(1, 0);
        prt.pivot = new Vector2(0.5f, 0);
        prt.anchoredPosition = Vector2.zero;
        prt.sizeDelta = new Vector2(0, 175);

        var textGo = new GameObject("HintText");
        textGo.transform.SetParent(panel.transform, false);
        hintText = textGo.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null) hintText.font = TMP_Settings.defaultFontAsset;
        hintText.fontSize = 26;
        hintText.alignment = TextAlignmentOptions.Left;
        hintText.color = Color.white;
        hintText.raycastTarget = false;
        hintText.margin = new Vector4(30, 8, 30, 8);
        var rt = textGo.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

    }
}