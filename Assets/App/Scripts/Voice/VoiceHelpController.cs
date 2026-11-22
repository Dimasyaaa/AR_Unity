using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Показывает подсказки голосовых команд и краткий результат их выполнения.</summary>
[RequireComponent(typeof(Button))]
public sealed class VoiceHelpController : MonoBehaviour
{
    /// <summary>Текущая панель голосовой помощи; маршрутизатор использует её для подсказок.</summary>
    public static VoiceHelpController Instance { get; private set; }

    [Header("Элементы панели подсказок")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI commandsText;
    [SerializeField] private Button closeButton;
    [SerializeField] private GameObject statusToast;
    [SerializeField] private TextMeshProUGUI statusText;

    // Храним корутину, чтобы новый ответ заменял предыдущий таймер скрытия.
    private Coroutine statusRoutine;

    /// <summary>Показывает, открыта ли панель подсказок в текущей сцене.</summary>
    public static bool IsOpen => Instance != null && Instance.panelRoot != null && Instance.panelRoot.activeSelf;

    /// <summary>Запоминает панель, подключает кнопки и скрывает элементы до первого открытия.</summary>
    private void Awake()
    {
        Instance = this;
        GetComponent<Button>().onClick.AddListener(Toggle);
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        if (panelRoot != null)
            panelRoot.SetActive(false);
        if (statusToast != null)
            statusToast.SetActive(false);
    }

    /// <summary>Очищает ссылку на сценовый объект при выгрузке сцены.</summary>
    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Открывает или закрывает подсказки по нажатию кнопки помощи.</summary>
    public void Toggle()
    {
        if (IsOpen)
            Close();
        else
            Open();
    }

    /// <summary>Собирает команды для активного экрана и показывает панель.</summary>
    public void Open()
    {
        if (panelRoot == null)
        {
            ShowFeedback("Голосовая помощь не настроена в сцене.");
            Debug.LogError("[Voice Help] Не назначен объект Panel Root в Inspector.");
            return;
        }

        RefreshCommands();
        panelRoot.SetActive(true);
    }

    /// <summary>Скрывает панель подсказок.</summary>
    public void Close()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    /// <summary>Закрывает подсказки, если они открыты; вызывается, например, перед съёмкой отчёта.</summary>
    public static void CloseIfOpen()
    {
        if (IsOpen)
            Instance.Close();
    }

    /// <summary>Обновляет список команд с учётом открытых карточек и доступных элементов экрана.</summary>
    public void RefreshCommands()
    {
        if (commandsText == null)
            return;

        commandsText.text = BuildCommandsText();
    }

    /// <summary>Показывает короткое сообщение о результате голосовой команды.</summary>
    public void ShowFeedback(string message)
    {
        if (statusToast == null || statusText == null || string.IsNullOrWhiteSpace(message))
            return;

        statusText.text = message;
        statusToast.SetActive(true);
        if (statusRoutine != null)
            StopCoroutine(statusRoutine);
        statusRoutine = StartCoroutine(HideStatusAfterDelay());
    }

    /// <summary>Скрывает сообщение после небольшой паузы.</summary>
    private System.Collections.IEnumerator HideStatusAfterDelay()
    {
        yield return new WaitForSeconds(3.5f);
        if (statusToast != null)
            statusToast.SetActive(false);
        statusRoutine = null;
    }

    /// <summary>Формирует подсказки по сцене, активным карточкам, кнопкам, полям и спискам.</summary>
    private string BuildCommandsText()
    {
        var lines = new List<string>();
        string scene = SceneManager.GetActiveScene().name;

        if (scene == "LoginScene")
        {
            lines.Add("АВТОРИЗАЦИЯ");
            lines.Add("Авторизация: «ФИО», затем «введи [имя]»; «пароль», затем «введи пароль [текст]».");
            lines.Add("Отдел: скажите «отдел», затем «вверх» / «вниз» и «выбрать»; или сразу «отдел [название]». Для входа скажите «войти».");
        }
        else if (scene == "StartScene")
        {
            lines.Add("ГЛАВНОЕ МЕНЮ");
            lines.Add("Главное меню: «начать инвентаризацию» открывает назначение и сканирование коробок; также доступны «инструкция», «синхронизировать», «Wi-Fi», «USB» и «выход».");
        }
        else if (scene == "InventorySelectionScene")
        {
            lines.Add("НАЗНАЧЕНИЕ ИНВЕНТАРИЗАЦИИ");
            lines.Add("Скажите «начать инвентаризацию», чтобы открыть сканер назначенной коробки, или «вернуться в меню».");
            lines.Add("Списки прокручиваются командами «прокрути список вверх/вниз». Для истории скажите «прокрути историю вверх/вниз».");
        }
        else
        {
            lines.Add("AR-СЦЕНА");
            lines.Add("Съёмка: «камера», затем «сделай фото».");
            lines.Add("AR-сцена: «сканировать QR», «главное меню», «меню объектов», «отмена», «удалить объекты».");
            lines.Add("Карточки: «калькулятор», «содержимое коробки», «3D-модели», «инвентаризация». Чтобы убрать одну, скажите «закрой карточку» или назовите её тип.");
            lines.Add("Размер: «увеличь карточку», «уменьши карточку», «сбрось размер»; можно назвать карточку, например «увеличь калькулятор».");
        }

        if (InventoryChecklistController.Active != null)
        {
            InventoryChecklistController inventory = InventoryChecklistController.Active;
            lines.Add("Инвентаризация: «есть [предмет]», «нет [предмет]», «завершить проверку». В отчёте: «сделай фото», «надиктуй заметку», «отправь отчёт».");
            if (inventory.VoiceItemNames.Count > 0)
            {
                string numberedItems = string.Join("; ", inventory.VoiceItemNames.Select((name, index) => $"{index + 1}. {name}"));
                lines.Add("Пункты чек-листа: " + numberedItems + ". Скажите, например, «есть веник» или «нет ведро».");
            }
        }
        if (FindObjectsByType<CalculatorController>(FindObjectsInactive.Include).Any(card => card.gameObject.activeInHierarchy))
            lines.Add("Калькулятор: цифры, «плюс», «минус», «умножить», «разделить», «равно», «очистить калькулятор».");
        if (FindObjectsByType<ImageCardController>(FindObjectsInactive.Include).Any(card => card.gameObject.activeInHierarchy))
            lines.Add("Фото/лист: «нажми лист 1», «нажми лист 2», «закрыть карточку».");
        var buttons = FindObjectsByType<Button>(FindObjectsInactive.Include)
            .Where(button => button != null && button.gameObject.activeInHierarchy && button.IsInteractable())
            .Where(button => button.GetComponent<VoiceHelpController>() == null && button.GetComponentInParent<VoiceHelpController>() == null)
            .Select(GetButtonLabel)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct()
            .ToArray();
        var fields = FindObjectsByType<TMP_InputField>(FindObjectsInactive.Include)
            .Where(field => field != null && field.gameObject.activeInHierarchy && field.interactable)
            .Select(GetInputLabel)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct()
            .ToArray();
        var dropdowns = FindObjectsByType<TMP_Dropdown>(FindObjectsInactive.Include)
            .Where(dropdown => dropdown != null && dropdown.gameObject.activeInHierarchy && dropdown.IsInteractable())
            .ToArray();

        if (buttons.Length > 0)
        {
            lines.Add("КНОПКИ НА ЭКРАНЕ");
            lines.Add(string.Join(" · ", buttons) + ". Скажите название кнопки или «нажми [название]».");
        }
        if (fields.Length > 0)
        {
            lines.Add("ПОЛЯ НА ЭКРАНЕ");
            lines.Add(string.Join(" · ", fields) + ". Скажите «поле [название]», затем «введи [текст]».");
        }
        if (dropdowns.Length > 0)
        {
            lines.Add("СПИСКИ НА ЭКРАНЕ");
            foreach (TMP_Dropdown dropdown in dropdowns)
            {
                string options = string.Join(" · ", dropdown.options.Select(option => option.text));
                lines.Add($"{GetDropdownLabel(dropdown)}: {options}. Скажите «выбери [вариант]».");
            }
        }

        lines.Add("Общие команды: «нажми [название кнопки]»; «прокрути вверх/вниз»; «помощь» открывает, а «закрой подсказки» закрывает это окно.");
        lines.Add("Для ввода: «поле [название]», затем «введи [текст]»; «очисти поле» стирает содержимое.");

        // Не добавляем пустые строки: на телефоне они занимают место, нужное подсказкам.
        return string.Join("\n", lines);
    }

    /// <summary>Возвращает подпись кнопки из TMP, обычного текста или имени объекта.</summary>
    private static string GetButtonLabel(Button button)
    {
        TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
        if (text != null && !string.IsNullOrWhiteSpace(text.text))
            return text.text.Trim();

        Text legacy = button.GetComponentInChildren<Text>(true);
        return legacy != null && !string.IsNullOrWhiteSpace(legacy.text)
            ? legacy.text.Trim()
            : button.gameObject.name;
    }

    /// <summary>Находит понятную подпись поля по его подсказке или имени объекта.</summary>
    private static string GetInputLabel(TMP_InputField field)
    {
        TMP_Text placeholder = field.placeholder as TMP_Text;
        if (placeholder != null && !string.IsNullOrWhiteSpace(placeholder.text))
            return placeholder.text.Trim();
        return field.gameObject.name;
    }

    /// <summary>Возвращает текущую подпись выпадающего списка.</summary>
    private static string GetDropdownLabel(TMP_Dropdown dropdown)
    {
        TMP_Text caption = dropdown.captionText;
        return caption != null && !string.IsNullOrWhiteSpace(caption.text)
            ? caption.text.Trim()
            : dropdown.gameObject.name;
    }
}
