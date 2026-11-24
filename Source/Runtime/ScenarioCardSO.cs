using System.Collections.Generic;
using UnityEngine;

public enum ScenarioCardRole
{
    Start,
    Expand,
    Terminal
}

[CreateAssetMenu(fileName = "ScenarioCard", menuName = "Scriptable Objects/Scenario Card")]
public sealed class ScenarioCardSO : ScriptableObject
{
    [SerializeField] private string displayName;
    [SerializeField] private ScenarioCardRole role = ScenarioCardRole.Expand;
    [SerializeField] private List<BlockData> candidates = new();

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public ScenarioCardRole Role => role;
    public IReadOnlyList<BlockData> Candidates => candidates;
}
