using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Вешается на кнопку "Button (Cube Debug)".
// Создаёт КАЛЬКУЛЯТОР перед камерой (не под полом) и держит его в единственном числе.
[RequireComponent(typeof(Button))]
public class CalculatorButtonHook : MonoBehaviour
{
    [SerializeField] private GameObject calculatorPrefab;
    [SerializeField] private float spawnDistance = 1.0f;

    void Start()
    {
        var button = GetComponent<Button>();

        // Отключаем старые слушатели (выбор индекса спавна и SelectionBox)
        for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(OnPressed);
    }

    private void OnPressed()
    {
        // Если калькулятор уже на сцене — не создаём второй, а возвращаем его перед камеру
        var existing = FindObjectOfType<CalculatorController>();
        if (existing != null)
        {
            PlaceInFront(existing.transform);
            return;
        }

        if (calculatorPrefab == null)
        {
            Debug.LogError("[Calc] Не назначен префаб калькулятора!");
            return;
        }

        var obj = Instantiate(calculatorPrefab);
        PlaceInFront(obj.transform);
    }

    // Ставит объект перед камерой вертикально, на удобной высоте (не под полом)
    private void PlaceInFront(Transform t)
    {
        var cam = Camera.main;
        if (cam == null) return;

        // Берём направление "вперёд" только по горизонтали, чтобы не смотреть под пол
        Vector3 forward = cam.transform.forward;
        forward.y = 0;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 pos = cam.transform.position + forward * spawnDistance;
        pos.y = cam.transform.position.y - 0.2f; // чуть ниже уровня глаз

        t.position = pos;
        t.rotation = Quaternion.LookRotation(cam.transform.position - pos);
    }
}