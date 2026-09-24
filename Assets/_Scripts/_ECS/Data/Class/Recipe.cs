using System;

// 시간 단위: 초
[Serializable]
public class Recipe : IData
{
    public int id;
    public string name;
    public string prefabName;
    public int timeLimit;
    public int nextRecipeSpawnDelay;
    public int spawnProbability;
    public int GetKey() => id;
}
