using UnityEngine;

/// <summary>Runtime identity used to prevent placing a second copy of an AR card prefab.</summary>
public sealed class ARPlacedCardMarker : MonoBehaviour
{
    public string PrefabName { get; set; }
}
