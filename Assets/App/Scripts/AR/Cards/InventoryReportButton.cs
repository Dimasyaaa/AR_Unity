using UnityEngine;

public sealed class InventoryReportButton : MonoBehaviour
{
    public void OpenCamera()
    {
        var checklist = InventoryChecklistController.Active;
        InventoryReportCapture.Open(checklist != null && checklist.CanFinalize ? checklist : null);
    }
}
