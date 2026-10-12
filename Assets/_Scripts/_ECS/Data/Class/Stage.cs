using System;

[Serializable]
public class Stage : IData
{
    public int id;
    public int stageNum;
    public int goal;
    public string tip;

    public int GetKey() => id;
}
