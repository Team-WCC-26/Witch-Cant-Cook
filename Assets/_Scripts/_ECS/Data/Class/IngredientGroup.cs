using System;

[Serializable]
public class IngredientGroup : IData
{
    public int grooviroom;
    public int difficulty;
    public int ingredient1;
    public int beltID;

    // A group can contain multiple rows. Page building uses GetAll(), not a key lookup.
    public int GetKey() => grooviroom;
}
