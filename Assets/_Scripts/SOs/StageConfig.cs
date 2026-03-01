using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "StageConfig", menuName = "Scriptable Objects/StageConfig")]
public class StageConfig : ScriptableObject
{
    public List<StageData> allStages = new();
}
