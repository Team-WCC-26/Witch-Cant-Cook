using System;

public class IngredientStat : IData
{
    public int id;
    public string name;
    public int hp;
    public float weight;
    public int damage;

    public int GetKey() => id;
    public IngredientStat() { }
}