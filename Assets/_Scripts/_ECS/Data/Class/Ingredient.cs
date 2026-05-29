using System;

public class Ingredient : CatchableData
{
    public int statID;
    public string throwing;
    public string tag;
    public byte conditionFlag;

    public Ingredient() : base() { } 
}