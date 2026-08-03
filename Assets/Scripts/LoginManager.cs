using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using ArInventory.Local;

// форма входа: отдел + ФИО + пароль
public class LoginManager : MonoBehaviour
{
    [Header("UI")]
    public TMP_Dropdown departmentDropdown;
    public TMP_InputField fullNameInput;
    public TMP_InputField passwordInput;
    public Button loginButton;
    public TMP_Text loginStatusText;

    private void Start()
    {
        // Гарантируем, что ApiClient существует
        //ApiClient.GetOrCreate();
        LocalClient.GetOrCreate();

        if (loginButton != null)
            loginButton.onClick.AddListener(OnLoginClicked);

        if (passwordInput != null)
        {
            passwordInput.contentType = TMP_InputField.ContentType.Password;
            passwordInput.ForceLabelUpdate();
        }

        LoadDepartments();
    }

    private void LoadDepartments()
    {
        SetStatus("Загрузка отделов...");

        //ApiClient.Instance.GetDepartments(
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

    private void OnLoginClicked()
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
        
        //ApiClient.Instance.Login(
        LocalClient.Instance.Login(
            fullName,
            department,
            password,
            onSuccess: response =>
            {
                SetStatus($"Вход выполнен: {response.fullName}");
                Debug.Log($"[Login] userId={response.userId}, department={response.department}");

                // задержка, чтобы пользователь увидел статус.
                Invoke(nameof(GoToStartScene), 0.8f);
            },
            onError: err =>
            {
                SetStatus(err);
                SetLoginEnabled(true);
            });
    }

    private void GoToStartScene()
    {
        SceneManager.LoadScene("StartScene");
    }

    private void SetLoginEnabled(bool enabled)
    {
        if (loginButton != null)
            loginButton.interactable = enabled;
    }

    private void SetStatus(string text)
    {
        if (loginStatusText != null)
            loginStatusText.text = text;

        Debug.Log("[Login] " + text);
    }
}