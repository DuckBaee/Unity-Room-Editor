using System.Collections.Generic;
using UnityEngine;

public enum ScenarioType
{
    Start,
    Connector,
    Objective,
    Cap,
    Exit,
    Intermediate,
}

[System.Serializable]
public class ScenarioStep
{
    [Tooltip("지정하면 카드의 이름/역할/후보를 사용합니다. 비워두면 기존 Scenario Type을 사용합니다.")]
    public ScenarioCardSO card;
    [Tooltip("Card를 지정하지 않은 단계에서만 사용합니다.")]
    public ScenarioType scenarioType;
    public bool uniqueAtDepth;

    public string DisplayName => card != null ? card.DisplayName : scenarioType.ToString();
    public bool IsStart => card != null ? card.Role == ScenarioCardRole.Start : scenarioType == ScenarioType.Start;
    public bool IsTerminal => card != null && card.Role == ScenarioCardRole.Terminal;
}

[CreateAssetMenu(fileName = "TemplateScenario", menuName = "Scriptable Objects/TemplateScenario")]
public class TemplateScenarioSO : ScriptableObject
{
    [SerializeField] private List<ScenarioStep> scenarioType = new();
    
    public int ScenarioCount => scenarioType != null ? scenarioType.Count : 0;
    
    public ScenarioStep GetStep(int index) => scenarioType[index];
    
}
