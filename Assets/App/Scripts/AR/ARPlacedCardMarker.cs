using UnityEngine;

/// <summary>Хранит тип префаба размещённой карточки для поиска и защиты от дубликатов.</summary>
public sealed class ARPlacedCardMarker : MonoBehaviour
{
    /// <summary>Имя исходного префаба, по которому спавнер распознаёт эту карточку.</summary>
    public string PrefabName { get; set; }
}
