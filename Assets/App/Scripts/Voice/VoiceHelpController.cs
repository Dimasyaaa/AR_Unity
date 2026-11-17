using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Controls the scene-authored voice help overlay and command feedback.</summary>
[RequireComponent(typeof(Button))]
public sealed class VoiceHelpController : MonoBehaviour
{
    public static VoiceHelpController Instance { get; private set; }

    [Header("Scene UI")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI commandsText;
    [SerializeField] private Button closeButton;
    [SerializeField] private GameObject statusToast;
    [SerializeField] private TextMeshProUGUI statusText;

    private Coroutine statusRoutine;

    public static bool IsOpen => Instance != null && Instance.panelRoot != null && Instance.panelRoot.activeSelf;

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

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Toggle()
    {
        if (IsOpen)
            Close();
        else
            Open();
    }

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

    public void Close()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    public static void CloseIfOpen()
    {
        if (IsOpen)
            Instance.Close();
    }

    public void RefreshCommands()
    {
        if (commandsText == null)
            return;

        commandsText.text = BuildCommandsText();
    }

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

    private System.Collections.IEnumerator HideStatusAfterDelay()
    {
        yield return new WaitForSeconds(3.5f);
        if (statusToast != null)
            statusToast.SetActive(false);
        statusRoutine = null;
    }

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
            lines.Add("Главное меню: «сканировать QR», «инструкция», «синхронизировать», «Wi-Fi», «USB», «выход».");
        }
        else
        {
            lines.Add("AR-СЦЕНА");
            lines.Add("Съёмка: «камера», затем «сделай фото».");
            lines.Add("AR-сцена: «сканировать QR», «главное меню», «меню объектов», «отмена», «удалить объекты».");
            lines.Add("Карточки: «калькулятор», «вид шкафа», «инвентаризация», «веб». Чтобы убрать одну, скажите «закрой карточку» или назовите её тип: «закрой калькулятор», «закрой шкаф», «закрой инвентаризацию».");
            lines.Add("Размер: «увеличь карточку», «уменьши карточку», «сбрось размер»; можно назвать карточку, например «увеличь калькулятор».");
        }

        if (InventoryChecklistController.Active != null)
        {
            InventoryChecklistController inventory = InventoryChecklistController.Active;
            lines.Add("Инвентаризация: «есть [предмет]», «нет [предмет]», «завершить проверку». Для отчёта сделайте фото и запишите голосовое сообщение. Запись останавливается кнопкой «Стоп».");
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
        if (WebOverlayController.Instance != null && WebOverlayController.Instance.IsOpen)
            lines.Add("Веб-окно: «приблизь страницу», «отдали страницу», «сдвинь веб-окно влево/вправо/вверх/вниз», «закрой веб».");

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

        // Keep commands on consecutive lines. Blank paragraphs made short phone screens
        // spend nearly half of the help view on spacing instead of instructions.
        return string.Join("\n", lines);
    }

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

    private static string GetInputLabel(TMP_InputField field)
    {
        TMP_Text placeholder = field.placeholder as TMP_Text;
        if (placeholder != null && !string.IsNullOrWhiteSpace(placeholder.text))
            return placeholder.text.Trim();
        return field.gameObject.name;
    }

    private static string GetDropdownLabel(TMP_Dropdown dropdown)
    {
        TMP_Text caption = dropdown.captionText;
        return caption != null && !string.IsNullOrWhiteSpace(caption.text)
            ? caption.text.Trim()
            : dropdown.gameObject.name;
    }
}
