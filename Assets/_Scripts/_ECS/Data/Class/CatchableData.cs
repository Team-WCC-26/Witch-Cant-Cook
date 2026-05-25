using System;

[Serializable]
public class CatchableData : IData
{
    public int id;
    public string name;
    public string prefabName;
    public int GetKey() => id;
    public CatchableData() { }
}