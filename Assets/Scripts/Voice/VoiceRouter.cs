using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Голосовые команды (RU + EN) + красивая экранная подсказка.
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

        // Помощь голосом: открыть/закрыть панель
        if (Has(t, "помощ", "help", "справк", "команд"))
        {
            if (VoiceHelpController.Instance != null) VoiceHelpController.Instance.Toggle();
            return;
        }

        // Если панель помощи открыта — «закрыть» закрывает именно её
        if (VoiceHelpController.IsOpen && Has(t, "закры", "close", "zakry"))
        {
            VoiceHelpController.CloseIfOpen();
            return;
        }

        try
        {
            if (scene == "StartScene") HandleStart(t);
            else if (scene == "LoginScene") HandleLogin(t);
            else HandleSample(t);
        }
        catch (Exception e)
        {
            lastAction = "ОШИБКА: " + e.Message;
            Debug.LogError($"[VoiceRouter] Ошибка при команде '{t}': {e}");
        }
    }

    private static bool Has(string t, params string[] keys)
    {
        foreach (var k in keys)
            if (t.Contains(k)) return true;
        return false;
    }

    // ================= START SCENE =================
    private void HandleStart(string t)
    {
        if (Has(t, "скан", "scan", "skan", "qr")) TryPress("ScanButton");
        else if (Has(t, "инструк", "instruction", "instruk", "help")) TryPress("InstructionsButton");
        else if (Has(t, "синхрон", "sync", "sinhron")) TryPress("SyncButton");
        else if (Has(t, "вай", "вифи", "wifi", "wi-fi", "интернет", "internet")) TryPress("WifiButton");
        else if (Has(t, "usb", "юсб", "усб", "флеш", "flash")) TryPress("UsbButton");
        else if (Has(t, "закры", "close", "zakry", "hide")) TryPress("CloseButton");
        else if (Has(t, "выход", "exit", "vyhod", "quit")) TryPress("ExitButton");
    }

    // ================= LOGIN SCENE =================
    private void HandleLogin(string t)
    {
        if (Has(t, "фио", "имя", "пользоват", "fam", "name", "fio", "surname"))
        { FocusInput("FullNameInput"); return; }

        if (Has(t, "парол", "parole", "password", "parol"))
        { FocusInput("PasswordInput"); return; }

        if (Has(t, "отдел", "department", "otdel", "section"))
        {
            var dd = FindDropdown("DepartmentDropdown");
            if (dd != null) { lastDropdown = dd; dd.Show(); lastAction = "→ открыт список отделов"; }
            else lastAction = "→ НЕ найден дропдаун отделов";
            return;
        }

        if (Has(t, "вниз", "down", "vniz")) { MoveDropdown(1); return; }
        if (Has(t, "вверх", "up", "vverh")) { MoveDropdown(-1); return; }

        if (Has(t, "выбра", "подтвер", "select", "choose", "apply") || t == "ок")
        {
            if (lastDropdown != null) { lastDropdown.Hide(); lastAction = "→ список закрыт"; }
            return;
        }

        if (Has(t, "войти", "вход", "login", "enter", "wait", "voyt"))
        {
            if (!TryPress("LoginButton")) TryPress("войти");
            return;
        }
    }

    private void MoveDropdown(int delta)
    {
        if (lastDropdown == null) lastDropdown = FindDropdown("DepartmentDropdown");
        if (lastDropdown != null)
        {
            lastDropdown.value = Mathf.Clamp(lastDropdown.value + delta, 0, lastDropdown.options.Count - 1);
            lastAction = "→ отдел: " + lastDropdown.captionText.text;
        }
        else lastAction = "→ нет активного списка";
    }

    // ================= SAMPLE SCENE =================
    private void HandleSample(string t)
    {
        // Служебные кнопки сцены
        if (Has(t, "далее", "продолж", "continue", "start", "начать"))
        { TryPress("Continue Button"); return; }

        if (Has(t, "меню", "создать", "menu", "create"))
        { TryPress("Create Button"); return; }

        if (Has(t, "отмена", "отменить", "cancel"))
        { TryPress("Cancel Button"); return; }

        if (Has(t, "удали", "remove", "delete"))
        { TryPress("Delete Button"); return; }

        if (Has(t, "настрой", "options", "доп"))
        { TryPress("Options Button"); return; }

        // Элементы Options Modal
        if (Has(t, "подсказ", "инструкция", "hint"))
        { TryPress("Hints Button"); return; }

        if (Has(t, "удал", "все", "hint"))
        { TryPress("Remove Objects Button"); return; }

        if (Has(t, "плоскост", "plane", "точки", "grid"))
        { TryPress("Debug Plane Toggle"); return; }

        if (Has(t, "дебаг", "debug", "отладк"))
        { TryPress("Debug Menu Toggle"); return; }

        // Диалог выбора веб-режима (появляется после «веб»)
        if (Has(t, "оверлей", "overlay", "экран", "screen"))
        { TryPress("оверлей"); return; }

        if (Has(t, "объект", "object", "obyekt", "ар"))
        { TryPress("объект"); return; }

        // Основные объекты (хуки ищутся ВКЛЮЧАЯ неактивные — Object Menu скрыт)
        if (Has(t, "калькулятор", "calculator", "kalk", "calc"))
        {
            var h = FindHook<CalculatorButtonHook>();
            if (h != null) { h.SpawnCalculator(); lastAction = "→ калькулятор"; }
            else TryPress("Button (Calculator)");
            return;
        }

        if (Has(t, "веб", "сайт", "web", "site", "veb", "sait"))
        {
            var h = FindHook<WebButtonHook>();
            if (h != null) { h.OpenWeb(); lastAction = "→ веб"; }
            else TryPress("Button (Web)");
            return;
        }

        if (Has(t, "карточ", "изображ", "фото", "card", "kart", "photo", "image"))
        {
            var h = FindHook<ImageButtonHook>();
            if (h != null) { h.SpawnCard(); lastAction = "→ карточка"; }
            else TryPress("Button (WebImg)");
            return;
        }

        if (Has(t, "положи", "закреп", "pin", "polozh", "fix")) { PinAll(true); return; }
        if (Has(t, "отпусти", "сними", "unpin", "release", "otpust")) { PinAll(false); return; }
        if (Has(t, "закры", "close", "zakry")) { CloseAll(); return; }
        if (Has(t, "назад", "back", "nazad")) { SceneManager.LoadScene("StartScene"); return; }
    }

    // Поиск (включая неактивные) и нажатие
    private T FindHook<T>() where T : Component
    {
        var all = UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include);
        return all.Length > 0 ? all[0] : null;
    }

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
}