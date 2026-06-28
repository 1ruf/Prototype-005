using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PoolSettings", menuName = "Game/Object Pool/Pool Settings")]
public sealed class PoolSettings : ScriptableObject
{
    [SerializeField] private List<PoolEntry> entries = new List<PoolEntry>();

    public IReadOnlyList<PoolEntry> Entries => entries;
}
