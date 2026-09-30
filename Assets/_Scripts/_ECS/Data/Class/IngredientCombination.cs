using System;

[Serializable]
public class IngredientCombination : IData
{
    public int id;
    public int recipeID;
    public int ingID;
    public byte conditionFlag;
    public int amount;
    public int GetKey() => id;
}
