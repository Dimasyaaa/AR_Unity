using System.Linq;
using UnityEngine;
using UnityEngine.XR.Templates.AR;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

/// <summary>Enforces one spawned instance per card type, including the template's built-in spawn menu.</summary>
public sealed class ARCardSpawnGuard : MonoBehaviour
{
    private ObjectSpawner spawner;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var currentSpawner = Object.FindAnyObjectByType<ObjectSpawner>();
        if (currentSpawner != null && currentSpawner.GetComponent<ARCardSpawnGuard>() == null)
            currentSpawner.gameObject.AddComponent<ARCardSpawnGuard>();
        ARCardSpawnUtility.DisablePlacement();
    }

    private void Awake()
    {
        spawner = GetComponent<ObjectSpawner>();
        if (spawner != null) spawner.objectSpawned += OnSpawned;
        foreach (var component in GetComponents<MonoBehaviour>())
        {
            if (component == null || component.GetType().Name != "ARInteractorSpawnTrigger") continue;
            component.GetType().GetProperty("requireHorizontalUpSurface")?.SetValue(component, false);
        }
    }

    private void OnSpawned(GameObject instance)
    {
        if (instance == null || spawner == null) return;
        int index = spawner.spawnOptionIndex;
        if (index < 0 || index >= spawner.objectPrefabs.Count) return;
        string prefabName = spawner.objectPrefabs[index].name;
        bool duplicate = Object.FindObjectsByType<ARPlacedCardMarker>(FindObjectsInactive.Include)
            .Any(marker => marker != null && marker.PrefabName == prefabName && marker.gameObject != instance);
        if (duplicate)
        {
            Destroy(instance);
            Debug.LogWarning($"[AR Cards] Уже размещена карточка «{prefabName}». Повторный экземпляр удалён.");
        }
        else
        {
            OrientCanvasesToPlacementSurface(instance);
            var marker = instance.GetComponent<ARPlacedCardMarker>() ?? instance.AddComponent<ARPlacedCardMarker>();
            marker.PrefabName = prefabName;
            EnableXRManipulation(instance);
        }

        ARCardSpawnUtility.DisablePlacement();
    }

    private static void EnableXRManipulation(GameObject card)
    {
        var body = card.GetComponent<Rigidbody>() ?? card.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        var grab = card.GetComponent<XRGrabInteractable>() ?? card.AddComponent<XRGrabInteractable>();
        grab.movementType = XRGrabInteractable.MovementType.Kinematic;
        grab.selectMode = InteractableSelectMode.Multiple;
        grab.throwOnDetach = false;
        grab.trackPosition = true;
        grab.trackRotation = true;
        grab.trackScale = true;

        var transformer = card.GetComponent<XRGeneralGrabTransformer>() ?? card.AddComponent<XRGeneralGrabTransformer>();
        transformer.allowTwoHandedScaling = true;
        transformer.allowTwoHandedRotation = XRGeneralGrabTransformer.TwoHandedRotationMode.FirstHandDirectedTowardsSecondHand;
        grab.AddMultipleGrabTransformer(transformer);
    }

    private static void OrientCanvasesToPlacementSurface(GameObject instance)
    {
        Vector3 surfaceNormal = instance.transform.up.normalized;
        Camera viewCamera = Camera.main;
        if (viewCamera != null && Vector3.Dot(surfaceNormal, viewCamera.transform.position - instance.transform.position) < 0f)
            surfaceNormal = -surfaceNormal;

        bool horizontalSurface = Mathf.Abs(Vector3.Dot(surfaceNormal, Vector3.up)) >= 0.75f;
        Vector3 canvasUp = horizontalSurface
            ? Vector3.ProjectOnPlane(instance.transform.forward, surfaceNormal)
            : Vector3.ProjectOnPlane(Vector3.up, surfaceNormal);

        if (canvasUp.sqrMagnitude < 0.0001f && viewCamera != null)
            canvasUp = Vector3.ProjectOnPlane(viewCamera.transform.up, surfaceNormal);
        if (canvasUp.sqrMagnitude < 0.0001f)
            canvasUp = Vector3.ProjectOnPlane(instance.transform.right, surfaceNormal);

        foreach (var canvas in instance.GetComponentsInChildren<Canvas>(true))
        {
            canvas.transform.rotation = Quaternion.LookRotation(surfaceNormal, canvasUp.normalized);
        }
    }

    private void OnDestroy()
    {
        if (spawner != null) spawner.objectSpawned -= OnSpawned;
    }
}
