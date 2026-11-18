using UnityEngine;

/// <summary>Находит префаб экрана отчёта и открывает его по запросу приложения.</summary>
[DisallowMultipleComponent]
public sealed class InventoryReportLauncher : MonoBehaviour
{
    [SerializeField] private InventoryReportCapture reportPrefab;

    private static InventoryReportLauncher instance;

    private void Awake()
    {
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    /// <summary>Создаёт экземпляр заранее собранного интерфейса и передаёт ему чек-лист.</summary>
    internal static void OpenReport(InventoryChecklistController checklist)
    {
        if (instance == null)
            instance = FindAnyObjectByType<InventoryReportLauncher>(FindObjectsInactive.Include);

        if (instance == null)
        {
            Debug.LogError("[Отчёт] В сцене не найден InventoryReportLauncher. Добавьте его на объект UI.");
            return;
        }

        if (instance.reportPrefab == null)
        {
            Debug.LogError("[Отчёт] В InventoryReportLauncher не назначен префаб экрана отчёта.");
            return;
        }

        InventoryReportCapture report = Instantiate(instance.reportPrefab);
        if (!report.Initialize(checklist))
            Destroy(report.gameObject);
    }
}
