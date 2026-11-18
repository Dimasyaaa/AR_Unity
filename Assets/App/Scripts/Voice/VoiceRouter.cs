using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Получает распознанные фразы и вызывает соответствующие действия активной сцены:
/// поля, списки, кнопки меню, AR-карточки и элементы отчёта.
/// </summary>
public sealed class VoiceRouter : MonoBehaviour
{
    // Слова-числа нужны для калькулятора и для продиктованных выражений.
    private static readonly Dictionary<string, string> DigitWords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["ноль"] = "0", ["нуль"] = "0", ["один"] = "1", ["одна"] = "1", ["одно"] = "1",
        ["два"] = "2", ["две"] = "2", ["три"] = "3", ["четыре"] = "4", ["пять"] = "5",
        ["шесть"] = "6", ["семь"] = "7", ["восемь"] = "8", ["девять"] = "9"
    };

    public static VoiceRouter Instance { get; private set; }

    // Последние выбранные элементы позволяют продолжать ввод и навигацию короткими командами.
    private TMP_Dropdown lastDropdown;
    private TMP_InputField lastInput;
    // Запоминаем исходный размер карточки, чтобы команда сброса возвращала её к нему.
    private readonly Dictionary<Transform, Vector3> originalCardScales = new Dictionary<Transform, Vector3>();

    /// <summary>Оставляет один маршрутизатор и создаёт компонент распознавания, если его ещё нет.</summary>
    private void Awake()
    {
        // На переходах между сценами сохраняется один маршрутизатор команд.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        if (VoiceController.Instance == null)
            gameObject.AddComponent<VoiceController>();
    }

    /// <summary>Подписывается на распознанные фразы и события загрузки сцен.</summary>
    private void OnEnable()
    {
        if (VoiceController.Instance != null)
            VoiceController.Instance.OnHeard += Handle;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    /// <summary>Снимает подписки, чтобы не обрабатывать одну фразу несколько раз.</summary>
    private void OnDisable()
    {
        if (VoiceController.Instance != null)
            VoiceController.Instance.OnHeard -= Handle;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>Очищает ссылку на экземпляр перед уничтожением объекта.</summary>
    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Сбрасывает ссылки на элементы старой сцены после перехода.</summary>
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        lastDropdown = null;
        lastInput = null;
        originalCardScales.Clear();
    }

    /// <summary>Разбирает фразу и передаёт её подходящему экрану или компоненту.</summary>
    private void Handle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return;

        // Сначала обрабатываем открытый отчёт и помощь, затем поля и списки,
        // после этого — действия AR-карточек.
        string phrase = raw.Trim();
        string normalized = Normalize(phrase);
        string scene = SceneManager.GetActiveScene().name;
        if (InventoryReportCapture.Active is { } report && report != null)
        {
            if (Has(normalized, "сделать фото", "сделай фото", "снимок")) report.TakePhoto();
            else if (Has(normalized, "записать голос", "запиши голос", "перезаписать")) report.RecordOrStop();
            else if (Has(normalized, "прослушать")) report.PlayVoice();
            else if (Has(normalized, "отправить отчёт", "отправить отчет", "отправь отчет", "отправь отчёт")) report.Send();
            else if (Has(normalized, "назад", "закрыть камеру", "отмена")) report.Close();
            return;
        }
        if (scene == "SampleScene" && (normalized == "камера" || normalized == "открой камеру" ||
            normalized == "открыть камеру"))
        {
            var activeChecklist = InventoryChecklistController.Active;
            InventoryReportCapture.Open(activeChecklist != null && activeChecklist.CanFinalize ? activeChecklist : null);
            return;
        }
        string action = null;
        bool containsDictatedText = IsTextEntryPhrase(normalized);

        if (VoiceHelpController.IsOpen && Has(normalized, "помощь", "справка", "команды", "подсказки", "закрой помощь", "закрой справку", "закрой подсказки", "закрыть помощь", "закрыть подсказки", "close help"))
        {
            VoiceHelpController.CloseIfOpen();
            Report("Подсказки закрыты");
            return;
        }

        if (Has(normalized, "помощь", "справка", "команды", "подсказки", "help"))
        {
            if (VoiceHelpController.Instance != null)
            {
                VoiceHelpController.Instance.Open();
                Report("Открыты команды для текущего экрана");
            }
            else Report("В этой сцене не настроена кнопка помощи");
            return;
        }

        if (TryHandleInput(phrase, normalized, out action))
        {
            Report(action);
            return;
        }

        if (TryHandleDropdown(normalized, out action))
        {
            Report(action);
            return;
        }

        if (TryHandleImagePage(normalized, out action))
        {
            Report(action);
            return;
        }

        if (TryHandleCardClose(normalized, out action))
        {
            Report(action);
            return;
        }

        InventoryChecklistController inventory = InventoryChecklistController.Active;
        if (inventory != null)
        {
            if (Has(normalized, "вверх", "прокрути вверх", "список вверх", "листай вверх"))
            {
                inventory.ScrollChecklistByVoice(true);
                Report("Список прокручен вверх");
                return;
            }
            if (Has(normalized, "вниз", "прокрути вниз", "список вниз", "листай вниз"))
            {
                inventory.ScrollChecklistByVoice(false);
                Report("Список прокручен вниз");
                return;
            }
            if (Has(normalized, "сохранить", "отправить проверку", "завершить проверку", "готово", "save"))
            {
                inventory.SaveByVoice();
                return;
            }
            if (inventory.SetStatusByVoice(normalized))
            {
                Report("Отметка предмета обновлена");
                return;
            }
        }

        if (TryHandleScale(normalized, out action))
        {
            Report(action);
            return;
        }

        if (TryHandleScroll(normalized, out action))
        {
            Report(action);
            return;
        }

        if (scene == "StartScene" && HandleStart(normalized, out action))
        {
            Report(action);
            return;
        }

        if (scene == "LoginScene" && HandleLogin(normalized, out action))
        {
            Report(action);
            return;
        }

        if (scene != "LoginScene" && scene != "StartScene" && HandleARScene(normalized, out action))
        {
            Report(action);
            return;
        }

        if (TryHandleCalculator(phrase, normalized, out action))
        {
            Report(action);
            return;
        }

        if (TryPressSpokenButton(normalized, out action))
        {
            Report(action);
            return;
        }

        Report(containsDictatedText
            ? "Сначала выберите поле голосом, например «ФИО» или «поиск»"
            : "Команда не распознана. Скажите «помощь», чтобы увидеть доступные команды.");
    }

    // Команды главного меню.
    private static bool HandleStart(string phrase, out string action)
    {
        if (Has(phrase, "сканировать", "скан", "qr", "scan"))
            return PressAny(new[] { "ScanButton", "Сканировать QR" }, out action);
        if (Has(phrase, "инструкция", "как пользоваться", "instruction"))
            return PressAny(new[] { "InstructionsButton", "Инструкция" }, out action);
        if (Has(phrase, "синхронизировать", "синхронизация", "sync"))
            return PressAny(new[] { "SyncButton", "Синхронизировать" }, out action);
        if (Has(phrase, "вай фай", "wifi", "wi fi", "интернет"))
            return PressAny(new[] { "WifiButton", "WiFi" }, out action);
        if (Has(phrase, "usb", "юсб", "усб", "флешка"))
            return PressAny(new[] { "UsbButton", "USB" }, out action);
        if (Has(phrase, "выход", "выйти", "завершить приложение", "exit"))
            return PressAny(new[] { "ExitButton", "Выход" }, out action);
        action = null;
        return false;
    }

    // Команды авторизации и выбора отдела.
    private bool HandleLogin(string phrase, out string action)
    {
        if (Has(phrase, "отдел", "подразделение", "department"))
        {
            TMP_Dropdown dropdown = FindDropdown("DepartmentDropdown");
            if (dropdown != null)
            {
                lastDropdown = dropdown;
                dropdown.Show();
                action = "Открыт список отделов";
            }
            else action = "Список отделов не найден";
            return true;
        }

        if (Has(phrase, "войти", "вход", "залогиниться", "login"))
            return PressAny(new[] { "LoginButton", "Войти" }, out action);

        action = null;
        return false;
    }

    // Команды сцены дополненной реальности и выбора карточек из меню.
    private bool HandleARScene(string phrase, out string action)
    {
        if (Has(phrase, "сканировать", "скан", "qr", "scan"))
            return PressAny(new[] { "Rescan QR Button", "Сканировать QR" }, out action);
        if (Has(phrase, "главное меню", "вернуться в меню", "назад", "back"))
            return PressAny(new[] { "Main Menu Button", "Главное меню" }, out action);
        if (Has(phrase, "далее", "продолжить", "начать", "continue", "start"))
            return PressAny(new[] { "Continue Button", "Начать" }, out action);
        if (Has(phrase, "меню объектов", "открыть меню", "создать", "menu"))
            return PressAny(new[] { "Create Button", "Создать" }, out action);
        if (Has(phrase, "отмена", "отменить", "cancel"))
            return PressAny(new[] { "Cancel Button", "Отмена" }, out action);
        if (Has(phrase, "удалить объекты", "удали все", "очистить сцену", "delete all"))
        {
            if (PressAny(new[] { "Delete Button", "Удалить" }, out action)) return true;
            CloseAllCards();
            action = "AR-карточки закрыты";
            return true;
        }
        if (Has(phrase, "настройки", "дополнительно", "options"))
            return PressAny(new[] { "Options Button", "Настройки" }, out action);
        if (Has(phrase, "подсказки", "плоскости", "debug plane"))
            return PressAny(new[] { "Hints Button", "Debug Plane Toggle" }, out action);
        if (Has(phrase, "дебаг", "отладка", "debug"))
            return PressAny(new[] { "Debug Menu Toggle" }, out action);

        if (Has(phrase, "калькулятор", "calculator", "посчитать", "calc"))
            return SelectCardButton("Button (Calculator)", out action);
        if (Has(phrase, "инвентаризация", "чек лист", "чеклист", "проверка шкафа"))
            return SelectCardButton("Button (Inventory)", out action);
        if (Has(phrase, "вид шкафа", "фото шкафа", "лист шкафа", "шкаф"))
            return SelectCardButton("Button (Cabinet)", out action);

        if (Has(phrase, "оверлей", "режим экрана", "overlay"))
            return PressAny(new[] { "оверлей", "Overlay" }, out action);
        if (Has(phrase, "режим объекта", "как объект", "object mode"))
            return PressAny(new[] { "объект", "Object" }, out action);
        if (Has(phrase, "карточка", "изображение"))
        {
            action = "Назовите карточку: «калькулятор», «вид шкафа» или «инвентаризация».";
            return true;
        }

        if (Has(phrase, "закрыть все", "закрой все", "убрать все карточки"))
        {
            CloseAllCards();
            action = "Все AR-карточки закрыты";
            return true;
        }
        action = null;
        return false;
    }

    // Выбирает поле ввода и подставляет в него продиктованный текст.
    private bool TryHandleInput(string raw, string phrase, out string action)
    {
        action = null;

        string entryValue;
        bool hasEntry = TryExtractEntryValue(raw, out entryValue);
        if (hasEntry)
        {
            string lowerValue = Normalize(entryValue);
            TMP_InputField target = null;
            if (lowerValue.StartsWith("пароль ", StringComparison.Ordinal))
            {
                target = FindInputByLabel("пароль");
                entryValue = entryValue.Substring(entryValue.IndexOf(' ') + 1).Trim();
            }
            else if (lowerValue.StartsWith("фио ", StringComparison.Ordinal) || lowerValue.StartsWith("имя ", StringComparison.Ordinal))
            {
                target = FindInputByLabel("фио");
                entryValue = entryValue.Substring(entryValue.IndexOf(' ') + 1).Trim();
            }
            else if (lowerValue.StartsWith("поиск ", StringComparison.Ordinal))
            {
                target = FindInputByLabel("поиск");
                entryValue = entryValue.Substring(entryValue.IndexOf(' ') + 1).Trim();
            }

            target ??= GetCurrentInput();
            if (target == null)
            {
                action = "Сначала выберите поле командой «поле [название]»";
                return true;
            }

            target.text = entryValue;
            target.caretPosition = target.text.Length;
            lastInput = target;
            action = IsPasswordField(target) ? "Пароль введён голосом" : $"Текст введён в поле «{GetInputLabel(target)}»";
            return true;
        }

        if (Has(phrase, "очистить поле", "очисти поле", "стереть текст", "clear field"))
        {
            TMP_InputField target = GetCurrentInput();
            if (target != null)
            {
                target.text = string.Empty;
                action = "Поле очищено";
            }
            else action = "Сначала выберите поле";
            return true;
        }

        if (phrase.StartsWith("поле ", StringComparison.Ordinal))
        {
            string label = phrase.Substring("поле ".Length).Trim();
            TMP_InputField target = FindInputByLabel(label);
            if (target != null)
            {
                target.Select();
                lastInput = target;
                action = "Выбрано поле «" + GetInputLabel(target) + "»";
            }
            else action = "Поле «" + label + "» не найдено";
            return true;
        }

        if (Has(phrase, "фио", "имя", "пользователь", "name", "full name"))
        {
            TMP_InputField target = FindInputByLabel("фио");
            if (target != null)
            {
                target.Select();
                lastInput = target;
                action = "Выбрано поле ФИО";
                return true;
            }
        }

        if (Has(phrase, "пароль", "password"))
        {
            TMP_InputField target = FindInputByLabel("пароль");
            if (target != null)
            {
                target.Select();
                lastInput = target;
                action = "Выбрано поле пароля";
                return true;
            }
        }

        if (Has(phrase, "поиск", "найти", "search"))
        {
            TMP_InputField target = FindInputByLabel("поиск");
            if (target != null)
            {
                target.Select();
                lastInput = target;
                action = "Выбрано поле поиска";
                return true;
            }
        }

        return false;
    }

    // Управляет открытым списком отделов или другим TMP_Dropdown на экране.
    private bool TryHandleDropdown(string phrase, out string action)
    {
        action = null;
        if (Has(phrase, "открой список", "открыть список", "открой отделы", "открой список отделов", "open list"))
        {
            TMP_Dropdown dropdown = Has(phrase, "отдел") ? FindDropdown("DepartmentDropdown") : FindVisibleDropdown();
            if (dropdown != null)
            {
                lastDropdown = dropdown;
                dropdown.Show();
                action = "Открыт список «" + GetDropdownLabel(dropdown) + "»";
            }
            else action = "На экране нет доступного списка";
            return true;
        }

        if (Has(phrase, "вверх", "выше", "up") || Has(phrase, "вниз", "ниже", "down"))
        {
            TMP_Dropdown dropdown = lastDropdown != null ? lastDropdown : FindDropdown("DepartmentDropdown");
            if (dropdown != null && dropdown.gameObject.activeInHierarchy)
            {
                int delta = Has(phrase, "вверх", "выше", "up") ? -1 : 1;
                dropdown.value = Mathf.Clamp(dropdown.value + delta, 0, dropdown.options.Count - 1);
                lastDropdown = dropdown;
                action = "Выбран отдел: " + dropdown.options[dropdown.value].text;
                return true;
            }
        }

        if (Has(phrase, "выбрать", "подтвердить", "применить", "select", "choose"))
        {
            if (lastDropdown != null)
            {
                lastDropdown.Hide();
                action = "Выбор подтверждён";
                lastDropdown = null;
                return true;
            }
        }

        if (phrase.StartsWith("выбери ", StringComparison.Ordinal) || phrase.StartsWith("отдел ", StringComparison.Ordinal))
        {
            string optionName = phrase.Substring(phrase.IndexOf(' ') + 1).Trim();
            TMP_Dropdown dropdown = lastDropdown != null ? lastDropdown : FindDropdownContainingOption(optionName);
            if (dropdown == null && phrase.StartsWith("отдел ", StringComparison.Ordinal))
                dropdown = FindDropdown("DepartmentDropdown");
            if (dropdown != null)
            {
                int index = FindDropdownOption(dropdown, optionName);
                if (index >= 0)
                {
                    dropdown.value = index;
                    dropdown.Hide();
                    lastDropdown = null;
                    action = "Выбран вариант: " + dropdown.options[index].text;
                }
                else action = "В списке нет варианта «" + optionName + "»";
                return true;
            }
        }

        return false;
    }

    // Меняет размер открытой карточки и при необходимости возвращает исходный масштаб.
    private bool TryHandleScale(string phrase, out string action)
    {
        action = null;
        bool reset = Has(phrase, "сбросить размер", "сбрось размер", "сбросить масштаб", "сбрось масштаб", "обычный размер", "верни размер", "reset zoom");
        bool enlarge = Has(phrase, "увеличить", "увеличь", "приблизить", "приблизь", "крупнее", "масштаб больше", "zoom in");
        bool reduce = Has(phrase, "уменьшить", "уменьши", "отдалить", "отдали", "мельче", "масштаб меньше", "zoom out");
        if (!reset && !enlarge && !reduce)
            return false;

        List<Transform> cards = GetOpenCardTransforms(phrase);
        if (cards.Count == 0)
        {
            action = "Сначала разместите AR-карточку";
            return true;
        }

        if (reset)
        {
            foreach (Transform card in cards)
                if (originalCardScales.TryGetValue(card, out Vector3 original))
                    card.localScale = original;
            action = "Размер карточек сброшен";
            return true;
        }

        float factor = Has(phrase, "в два раза", "вдвое", "twice") ? (enlarge ? 2f : .5f) : (enlarge ? 1.15f : .87f);
        foreach (Transform card in cards)
        {
            if (!originalCardScales.ContainsKey(card))
                originalCardScales[card] = card.localScale;

            Vector3 next = card.localScale * factor;
            float largestAxis = Mathf.Max(Mathf.Abs(next.x), Mathf.Abs(next.y), Mathf.Abs(next.z));
            if (largestAxis > 2.5f) next *= 2.5f / largestAxis;
            else if (largestAxis < .35f) next *= .35f / Mathf.Max(largestAxis, .0001f);
            card.localScale = next;
        }
        string targetName = GetCardTargetLabel(phrase);
        action = (enlarge ? "Увеличена карточка «" : "Уменьшена карточка «") + targetName + "»";
        return true;
    }

    // Переключает листы карточки шкафа или изображения.
    private bool TryHandleImagePage(string phrase, out string action)
    {
        action = null;
        bool next = Has(phrase, "следующий лист", "лист следующий", "лист два", "лист 2", "страница два", "страница 2");
        bool previous = Has(phrase, "предыдущий лист", "лист предыдущий", "лист один", "лист 1", "страница один", "страница 1");
        if (!next && !previous)
            return false;

        ImageCardController card = FindObjectsByType<ImageCardController>(FindObjectsInactive.Include)
            .FirstOrDefault(item => item != null && item.gameObject.activeInHierarchy);
        if (card == null)
        {
            action = "Сначала откройте карточку шкафа или изображения";
            return true;
        }

        bool selected = card.SelectPageFromVoice(next ? 1 : 0);
        if (selected)
            action = next ? "Открыт лист 2" : "Открыт лист 1";
        else
            action = next ? "Лист 2 не добавлен для этого шкафа" : "Лист 1 не добавлен для этого шкафа";
        return true;
    }

    // Закрывает названную карточку либо последнюю открытую карточку.
    private static bool TryHandleCardClose(string phrase, out string action)
    {
        action = null;
        bool isCloseCommand = Has(phrase, "закрыть карточку", "закрой карточку", "закрыть панель", "закрой панель",
            "закрой калькулятор", "закрыть калькулятор", "закрой инвентаризацию", "закрыть инвентаризацию",
            "закрой шкаф", "закрыть шкаф", "закрой фото", "закрыть фото",
            "close card", "close panel");
        if (!isCloseCommand)
            return false;

        bool targetCalculator = Has(phrase, "калькулятор", "calc");
        bool targetInventory = Has(phrase, "инвентаризац", "чек лист", "чеклист");
        bool targetCabinet = Has(phrase, "шкаф", "фото");
        bool hasTarget = targetCalculator || targetInventory || targetCabinet;

        var candidates = new List<GameObject>();
        foreach (CalculatorController card in FindObjectsByType<CalculatorController>(FindObjectsInactive.Include))
            if (card != null && card.gameObject.activeInHierarchy && (!hasTarget || targetCalculator)) candidates.Add(card.gameObject);
        foreach (InventoryChecklistController card in FindObjectsByType<InventoryChecklistController>(FindObjectsInactive.Include))
            if (card != null && card.gameObject.activeInHierarchy && (!hasTarget || targetInventory)) candidates.Add(card.gameObject);
        foreach (ImageCardController card in FindObjectsByType<ImageCardController>(FindObjectsInactive.Include))
            if (card != null && card.gameObject.activeInHierarchy && (!hasTarget || targetCabinet)) candidates.Add(card.gameObject);
        GameObject cardToClose = candidates.LastOrDefault();
        if (cardToClose == null)
        {
            action = hasTarget ? "Карточка этого типа не открыта" : "Нет открытых карточек";
            return true;
        }

        ARCardModeSelector.CloseCard(cardToClose);
        action = "Карточка закрыта";
        return true;
    }

    // Прокручивает доступный на текущем экране ScrollRect.
    private bool TryHandleScroll(string phrase, out string action)
    {
        action = null;
        bool down = Has(phrase, "прокрути вниз", "листай вниз", "список вниз", "scroll down");
        bool up = Has(phrase, "прокрути вверх", "листай вверх", "список вверх", "scroll up");
        if (!down && !up)
            return false;

        ScrollRect scroll = FindObjectsByType<ScrollRect>(FindObjectsInactive.Include)
            .Where(item => item != null && item.isActiveAndEnabled && item.gameObject.activeInHierarchy)
            .OrderByDescending(item => item.viewport != null && item.viewport.gameObject.activeInHierarchy)
            .FirstOrDefault();
        if (scroll == null)
        {
            action = "На экране нет прокручиваемого списка";
            return true;
        }

        scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + (up ? .28f : -.28f));
        action = up ? "Список прокручен вверх" : "Список прокручен вниз";
        return true;
    }

    // Передаёт в калькулятор отдельную команду кнопки или целое выражение.
    private bool TryHandleCalculator(string raw, string phrase, out string action)
    {
        action = null;
        CalculatorController calculator = FindObjectsByType<CalculatorController>(FindObjectsInactive.Include)
            .FirstOrDefault(item => item != null && item.gameObject.activeInHierarchy);
        if (calculator == null)
            return false;

        if (TryParseSpokenExpression(raw, out string expression))
        {
            calculator.EvaluateVoiceExpression(expression);
            action = "Вычисление выполнено";
            return true;
        }

        string key = null;
        if (Has(phrase, "очистить калькулятор", "сбросить калькулятор", "стереть всё")) key = "C";
        else if (Has(phrase, "разделить", "делить", "деление", "поделить", "÷")) key = "/";
        else if (Has(phrase, "умножить", "умножение", "times", "звёздочка")) key = "*";
        else if (Has(phrase, "плюс", "прибавить", "plus")) key = "+";
        else if (Has(phrase, "минус", "вычесть", "minus")) key = "-";
        else if (Has(phrase, "равно", "посчитать", "вычислить", "результат", "equals")) key = "=";
        else if (DigitWords.TryGetValue(phrase, out string digit)) key = digit;
        else if (phrase.Length == 1 && "0123456789+-*/=c".Contains(phrase)) key = phrase == "c" ? "C" : phrase;

        if (key == null)
            return false;

        calculator.PressFromVoice(key);
        action = key == "/" ? "В калькулятор добавлено деление" : "Команда калькулятора выполнена";
        return true;
    }

    // Преобразует произнесённый пример в строку вроде 80/8 для калькулятора.
    private static bool TryParseSpokenExpression(string raw, out string expression)
    {
        string value = raw.ToLowerInvariant().Trim();
        value = Regex.Replace(value, @"\b(посчитай|вычисли|реши|пример|сколько будет)\b", " ");
        value = value.Replace("умножить на", "*").Replace("умножить", "*").Replace("помножить на", "*")
            .Replace("разделить на", "/").Replace("поделить на", "/").Replace("делить на", "/")
            .Replace("разделить", "/").Replace("поделить", "/").Replace("делить", "/")
            .Replace("плюс", "+").Replace("минус", "-").Replace("запятая", ".");

        foreach (var pair in DigitWords.OrderByDescending(pair => pair.Key.Length))
            value = Regex.Replace(value, @"\b" + Regex.Escape(pair.Key) + @"\b", pair.Value, RegexOptions.IgnoreCase);

        value = Regex.Replace(value, @"[^0-9+*/.\-]", string.Empty);
        bool isExpression = Regex.IsMatch(value, @"^\d+(?:\.\d+)?[+*/\-]\d+(?:\.\d+)?$");
        expression = isExpression ? value : null;
        return isExpression;
    }

    // Последний общий способ: найти кнопку по её подписи и вызвать её событие.
    private bool TryPressSpokenButton(string phrase, out string action)
    {
        string target = phrase;
        bool explicitPress = target.StartsWith("нажми ", StringComparison.Ordinal) || target.StartsWith("нажать ", StringComparison.Ordinal) ||
            target.StartsWith("кнопка ", StringComparison.Ordinal) || target.StartsWith("выбери кнопку ", StringComparison.Ordinal);
        string[] prefixes = { "выбери кнопку ", "нажми кнопку ", "нажать кнопку ", "кнопка ", "нажми ", "нажать " };
        foreach (string prefix in prefixes)
            if (target.StartsWith(prefix, StringComparison.Ordinal))
            {
                target = target.Substring(prefix.Length).Trim();
                break;
            }

        if (string.IsNullOrWhiteSpace(target))
        {
            action = null;
            return false;
        }

        Button best = null;
        int bestScore = 0;
        foreach (Button button in FindObjectsByType<Button>(FindObjectsInactive.Include))
        {
            if (button == null || !button.gameObject.activeInHierarchy || !button.IsInteractable()) continue;
            string label = Normalize(GetButtonText(button));
            string objectName = Normalize(button.gameObject.name);
            int score = 0;
            if (label == target || objectName == target) score = 100;
            else if (explicitPress && (label.Contains(target) || target.Contains(label) || objectName.Contains(target))) score = 70;
            else if (!explicitPress && target.Length >= 4 && label.Length >= 4 && label.Contains(target)) score = 50;

            if (button.gameObject.activeInHierarchy) score += 20;
            if (score > bestScore)
            {
                best = button;
                bestScore = score;
            }
        }

        if (best != null && bestScore >= 50)
        {
            best.onClick.Invoke();
            action = "Нажата кнопка «" + GetButtonText(best) + "»";
            return true;
        }

        action = null;
        return false;
    }

    /// <summary>Возвращает выбранное поле либо ранее выбранное поле, если оно ещё доступно.</summary>
    private TMP_InputField GetCurrentInput()
    {
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        TMP_InputField selectedInput = selected != null ? selected.GetComponent<TMP_InputField>() : null;
        if (selectedInput != null && selectedInput.interactable)
            return selectedInput;
        if (lastInput != null && lastInput.gameObject.activeInHierarchy && lastInput.interactable)
            return lastInput;
        return null;
    }

    /// <summary>Проверяет, начинается ли фраза с команды ввода текста.</summary>
    private static bool IsTextEntryPhrase(string phrase) =>
        phrase.StartsWith("введи ", StringComparison.Ordinal) || phrase.StartsWith("введите ", StringComparison.Ordinal) ||
        phrase.StartsWith("напиши ", StringComparison.Ordinal) || phrase.StartsWith("впиши ", StringComparison.Ordinal) ||
        phrase.StartsWith("диктуй ", StringComparison.Ordinal) || phrase.StartsWith("type ", StringComparison.Ordinal);

    /// <summary>Убирает командное слово и возвращает текст, который нужно поместить в поле.</summary>
    private static bool TryExtractEntryValue(string raw, out string value)
    {
        string trimmed = raw.Trim();
        string[] prefixes = { "введи ", "введите ", "напиши ", "впиши ", "диктуй ", "type " };
        foreach (string prefix in prefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = trimmed.Substring(prefix.Length).Trim();
                return value.Length > 0;
            }
        }
        value = null;
        return false;
    }

    /// <summary>Находит активное поле ввода по подписи, имени или части названия.</summary>
    private static TMP_InputField FindInputByLabel(string requestedLabel)
    {
        string target = Normalize(requestedLabel);
        return FindObjectsByType<TMP_InputField>(FindObjectsInactive.Include)
            .Where(field => field != null && field.gameObject.activeInHierarchy && field.interactable)
            .OrderByDescending(field =>
            {
                string label = Normalize(GetInputLabel(field));
                string name = Normalize(field.gameObject.name);
                return label == target || name == target ? 100 :
                    label.Contains(target) || name.Contains(target) || target.Contains(label) ? 50 : 0;
            })
            .FirstOrDefault(field => Normalize(GetInputLabel(field)).Contains(target) || Normalize(field.gameObject.name).Contains(target) || target.Contains(Normalize(GetInputLabel(field))));
    }

    /// <summary>Возвращает подсказку поля или его имя в сцене.</summary>
    private static string GetInputLabel(TMP_InputField field)
    {
        TMP_Text placeholder = field.placeholder as TMP_Text;
        if (placeholder != null && !string.IsNullOrWhiteSpace(placeholder.text)) return placeholder.text.Trim();
        return field.gameObject.name;
    }

    /// <summary>Определяет поле пароля, чтобы не выводить его содержимое в сообщениях.</summary>
    private static bool IsPasswordField(TMP_InputField field)
    {
        return field.contentType == TMP_InputField.ContentType.Password || Normalize(GetInputLabel(field)).Contains("пароль");
    }

    /// <summary>Ищет активный выпадающий список по имени объекта.</summary>
    private TMP_Dropdown FindDropdown(string name)
    {
        return FindObjectsByType<TMP_Dropdown>(FindObjectsInactive.Include)
            .FirstOrDefault(dropdown => dropdown != null && dropdown.gameObject.activeInHierarchy && Normalize(dropdown.gameObject.name).Contains(Normalize(name)));
    }

    /// <summary>Находит доступный выпадающий список на текущем экране.</summary>
    private static TMP_Dropdown FindVisibleDropdown()
    {
        return FindObjectsByType<TMP_Dropdown>(FindObjectsInactive.Include)
            .FirstOrDefault(dropdown => dropdown != null && dropdown.gameObject.activeInHierarchy && dropdown.IsInteractable());
    }

    /// <summary>Находит список, содержащий вариант с произнесённым названием.</summary>
    private static TMP_Dropdown FindDropdownContainingOption(string requested)
    {
        string target = Normalize(requested);
        return FindObjectsByType<TMP_Dropdown>(FindObjectsInactive.Include)
            .Where(dropdown => dropdown != null && dropdown.gameObject.activeInHierarchy && dropdown.IsInteractable())
            .OrderByDescending(dropdown => dropdown.options.Any(option => Normalize(option.text) == target) ? 100 :
                dropdown.options.Any(option => Normalize(option.text).Contains(target) || target.Contains(Normalize(option.text))) ? 50 : 0)
            .FirstOrDefault(dropdown => dropdown.options.Any(option => Normalize(option.text) == target || Normalize(option.text).Contains(target) || target.Contains(Normalize(option.text))));
    }

    /// <summary>Возвращает понятное название списка для сообщения пользователю.</summary>
    private static string GetDropdownLabel(TMP_Dropdown dropdown)
    {
        string objectName = Normalize(dropdown.gameObject.name);
        if (objectName.Contains("department")) return "Отделы";
        return dropdown.captionText != null && !string.IsNullOrWhiteSpace(dropdown.captionText.text)
            ? dropdown.captionText.text.Trim()
            : dropdown.gameObject.name;
    }

    /// <summary>Возвращает индекс точного или ближайшего совпадения среди вариантов списка.</summary>
    private static int FindDropdownOption(TMP_Dropdown dropdown, string requested)
    {
        string target = Normalize(requested);
        int exact = dropdown.options.FindIndex(option => Normalize(option.text) == target);
        if (exact >= 0) return exact;
        return dropdown.options.FindIndex(option => Normalize(option.text).Contains(target) || target.Contains(Normalize(option.text)));
    }

    /// <summary>Пробует найти и нажать кнопку по нескольким возможным именам.</summary>
    private static bool PressAny(IEnumerable<string> names, out string action)
    {
        foreach (string name in names)
            if (TryPressButton(name, out action)) return true;
        action = null;
        return false;
    }

    /// <summary>Находит доступную кнопку по имени объекта или тексту на ней и вызывает её событие.</summary>
    private static bool TryPressButton(string target, out string action)
    {
        string normalizedTarget = Normalize(target);
        Button button = FindObjectsByType<Button>(FindObjectsInactive.Include)
            .Where(item => item != null && item.gameObject.activeInHierarchy && item.IsInteractable())
            .OrderByDescending(item =>
            {
                string name = Normalize(item.gameObject.name);
                string label = Normalize(GetButtonText(item));
                int score = name == normalizedTarget || label == normalizedTarget ? 100 :
                    name.Contains(normalizedTarget) || label.Contains(normalizedTarget) || normalizedTarget.Contains(name) ? 50 : 0;
                if (item.gameObject.activeInHierarchy) score += 20;
                return score;
            })
            .FirstOrDefault(item =>
            {
                string name = Normalize(item.gameObject.name);
                string label = Normalize(GetButtonText(item));
                return name == normalizedTarget || label == normalizedTarget || name.Contains(normalizedTarget) || label.Contains(normalizedTarget);
            });

        if (button == null)
        {
            action = "Не найдена кнопка «" + target + "»";
            return false;
        }

        button.onClick.Invoke();
        action = "Нажата кнопка «" + GetButtonText(button) + "»";
        return true;
    }

    // Находит кнопку по имени даже в закрытом меню и использует её общий обработчик.
    private static bool SelectCardButton(string buttonName, out string action)
    {
        ARCardButtonHook button = FindObjectsByType<ARCardButtonHook>(FindObjectsInactive.Include)
            .FirstOrDefault(hook => hook != null && hook.gameObject.name == buttonName);
        if (button == null)
        {
            action = "Карточка «" + buttonName + "» не добавлена в меню сцены";
            return false;
        }

        button.SelectFromVoice();
        action = "Выбрана AR-карточка «" + GetButtonText(button.GetComponent<Button>()) + "»";
        return true;
    }

    /// <summary>Возвращает текст кнопки из TMP, обычного текста или имени объекта.</summary>
    private static string GetButtonText(Button button)
    {
        if (button == null) return "";
        TMP_Text tmp = button.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null && !string.IsNullOrWhiteSpace(tmp.text)) return tmp.text.Trim();
        Text legacy = button.GetComponentInChildren<Text>(true);
        return legacy != null && !string.IsNullOrWhiteSpace(legacy.text) ? legacy.text.Trim() : button.gameObject.name;
    }

    /// <summary>Собирает открытые карточки, на которые должна повлиять команда изменения размера.</summary>
    private static List<Transform> GetOpenCardTransforms(string phrase)
    {
        var cards = new HashSet<Transform>();
        bool targetCalculator = Has(phrase, "калькулятор", "calculator");
        bool targetInventory = Has(phrase, "инвентаризац", "чек лист", "чеклист", "проверка шкафа");
        bool targetCabinet = Has(phrase, "шкаф", "фото", "лист");
        bool hasTarget = targetCalculator || targetInventory || targetCabinet;
        foreach (CalculatorController card in FindObjectsByType<CalculatorController>(FindObjectsInactive.Include))
            if (card != null && card.gameObject.activeInHierarchy && (!hasTarget || targetCalculator)) cards.Add(card.transform);
        foreach (ImageCardController card in FindObjectsByType<ImageCardController>(FindObjectsInactive.Include))
            if (card != null && card.gameObject.activeInHierarchy && (!hasTarget || targetCabinet || targetInventory)) cards.Add(card.transform);
        foreach (InventoryChecklistController card in FindObjectsByType<InventoryChecklistController>(FindObjectsInactive.Include))
            if (card != null && card.gameObject.activeInHierarchy && (!hasTarget || targetInventory)) cards.Add(card.transform);
        return cards.ToList();
    }

    /// <summary>Определяет название карточки для подтверждающего сообщения.</summary>
    private static string GetCardTargetLabel(string phrase)
    {
        if (Has(phrase, "калькулятор", "calculator")) return "Калькулятор";
        if (Has(phrase, "инвентаризац", "чек лист", "чеклист")) return "Инвентаризация";
        if (Has(phrase, "шкаф", "фото", "лист")) return "Шкаф";
        return "все открытые AR";
    }

    // Закрывает все активные карточки перед выходом из AR-сцены.
    private static void CloseAllCards()
    {
        foreach (CalculatorController card in FindObjectsByType<CalculatorController>(FindObjectsInactive.Include))
            if (card != null) ARCardModeSelector.CloseCard(card.gameObject);
        foreach (ImageCardController card in FindObjectsByType<ImageCardController>(FindObjectsInactive.Include))
            if (card != null) ARCardModeSelector.CloseCard(card.gameObject);
        foreach (InventoryChecklistController card in FindObjectsByType<InventoryChecklistController>(FindObjectsInactive.Include))
            if (card != null) ARCardModeSelector.CloseCard(card.gameObject);
    }

    /// <summary>Приводит текст к нижнему регистру и заменяет знаки препинания пробелами.</summary>
    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+", " ").Trim();
    }

    /// <summary>Проверяет, содержит ли нормализованная фраза один из заданных вариантов команды.</summary>
    private static bool Has(string phrase, params string[] parts)
    {
        foreach (string part in parts)
            if (phrase.Contains(Normalize(part))) return true;
        return false;
    }

    // Показывает краткий результат команды в подсказке и журнале Unity.
    private static void Report(string action)
    {
        if (VoiceHelpController.Instance != null)
            VoiceHelpController.Instance.ShowFeedback(action);
        Debug.Log("[Voice] " + action);
    }
}
