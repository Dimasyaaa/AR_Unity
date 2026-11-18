using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using ArInventory.Local;

/// <summary>Загружает отделы из локальной базы, проверяет форму и выполняет вход.</summary>
public class LoginManager : MonoBehaviour
{
    [Header("Поля формы авторизации")]
    [SerializeField] private TMP_Dropdown departmentDropdown;
    [SerializeField] private TMP_InputField fullNameInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private Button loginButton;
    [SerializeField] private TMP_Text loginStatusText;

    /// <summary>Подготавливает локальные данные и загружает список отделов.</summary>
    private void Start()
    {
        // Событие обычно назначено у кнопки в сцене. Подключаем запасной обработчик,
        // если Unity загрузила старую сцену без сохранённого OnClick.
        if (loginButton != null && loginButton.onClick.GetPersistentEventCount() == 0)
        {
            loginButton.onClick.AddListener(SignIn);
            Debug.LogWarning("[Login] Для кнопки входа включён резервный обработчик OnClick.");
        }

        // Создаем и открываем локальную базу данных.
        LocalDatabase.GetOrCreate();

        // Создаем клиент локальных данных.
        LocalClient.GetOrCreate();

        // Ждём готовности локальной базы, чтобы заполнить список отделов.
        LocalDatabase.OnDatabaseReady += LoadDepartments;

        // Если база уже готова, список можно загрузить сразу.
        if (LocalDatabase.Instance != null && LocalDatabase.Instance.IsReady)
        {
            LoadDepartments();
        }
        else
        {
            SetStatus("Подготовка базы данных...");
        }
    }

    /// <summary>Снимает подписку на готовность базы при закрытии сцены авторизации.</summary>
    private void OnDestroy()
    {
        LocalDatabase.OnDatabaseReady -= LoadDepartments;
    }

    /// <summary>Заполняет выпадающий список отделами из локальной базы.</summary>
    private void LoadDepartments()
    {
        // Список формируется из локальной базы, чтобы вход работал и без сети.
        SetStatus("Загрузка отделов...");

        LocalClient.Instance.GetDepartments(
            onSuccess: names =>
            {
                if (departmentDropdown == null)
                    return;

                departmentDropdown.ClearOptions();
                departmentDropdown.AddOptions(names);

                if (names.Count > 0)
                {
                    departmentDropdown.value = 0;
                    SetStatus("Выберите отдел и нажмите «Войти»");
                }
                else
                {
                    SetStatus("В базе нет пользователей и отделов");
                }
            },
            onError: err => SetStatus(err)
        );
    }

    /// <summary>Проверяет поля формы и запрашивает проверку учётных данных у LocalClient.</summary>
    public void SignIn()
    {
        if (departmentDropdown == null ||
            fullNameInput == null ||
            passwordInput == null)
        {
            SetStatus("Не все поля формы назначены в Inspector");
            return;
        }

        if (departmentDropdown.options.Count == 0)
        {
            SetStatus("Список отделов пуст");
            return;
        }

        string fullName = fullNameInput.text.Trim();
        string password = passwordInput.text;

        if (string.IsNullOrEmpty(fullName))
        {
            SetStatus("Введите ФИО");
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            SetStatus("Введите пароль");
            return;
        }

        string department =
            departmentDropdown.options[departmentDropdown.value].text;

        SetStatus("Выполняется вход...");
        SetLoginEnabled(false);
        
        LocalClient.Instance.Login(
            fullName,
            department,
            password,
            onSuccess: response =>
            {
                SetStatus($"Вход выполнен: {response.fullName}");
                Debug.Log($"[Login] userId={response.userId}, department={response.department}");

                // Небольшая задержка даёт время прочитать сообщение об успешном входе.
                Invoke(nameof(GoToStartScene), 0.8f);
            },
            onError: err =>
            {
                SetStatus(err);
                SetLoginEnabled(true);
            });
    }

    /// <summary>Переходит в главное меню после успешной авторизации.</summary>
    private void GoToStartScene()
    {
        // Вызывается с небольшой задержкой после успешной проверки учётных данных.
        SceneManager.LoadScene("StartScene");
    }

    /// <summary>Блокирует повторную отправку формы на время входа.</summary>
    private void SetLoginEnabled(bool enabled)
    {
        // Пока идёт запрос входа, повторное нажатие не должно отправить его ещё раз.
        if (loginButton != null)
            loginButton.interactable = enabled;
    }

    /// <summary>Показывает состояние авторизации в форме и пишет его в Console.</summary>
    private void SetStatus(string text)
    {
        // Один метод обновляет надпись формы и оставляет ту же информацию в Console.
        if (loginStatusText != null)
            loginStatusText.text = text;

        Debug.Log("[Login] " + text);
    }
}
