using UnityEngine;

/// <summary>Открывает экран завершения инвентаризации по нажатию заранее созданной кнопки.</summary>
public sealed class InventoryReportButton : MonoBehaviour
{
    public void OpenCamera()
    {
        // Отчёт можно привязать к чек-листу, только если все его пункты уже отмечены.
        var checklist = InventoryChecklistController.Active;
        InventoryReportCapture.Open(checklist != null && checklist.CanFinalize ? checklist : null);
    }
}
