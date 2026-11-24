using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "BlockLibrary", menuName = "Scriptable Objects/Block Library")]
public sealed class BlockLibrarySO : ScriptableObject
{
    [SerializeField] private List<BlockData> startBlockData = new();
    [SerializeField] private List<BlockData> connectorBlockData = new();
    [SerializeField] private List<BlockData> objectiveBlockData = new();
    [SerializeField] private List<BlockData> intermediateBlockData = new();
    [SerializeField] private List<BlockData> exitBlockData = new();
    [SerializeField] private List<BlockData> capBlockData = new();

    public IReadOnlyList<BlockData> GetPool(ScenarioType type)
    {
        return type switch
        {
            ScenarioType.Start => startBlockData,
            ScenarioType.Connector => connectorBlockData,
            ScenarioType.Objective => objectiveBlockData,
            ScenarioType.Intermediate => intermediateBlockData,
            ScenarioType.Exit => exitBlockData,
            ScenarioType.Cap => capBlockData,
            _ => Array.Empty<BlockData>()
        };
    }
}
