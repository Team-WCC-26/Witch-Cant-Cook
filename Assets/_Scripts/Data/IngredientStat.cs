using UnityEngine;

public class IngredientStat : IData
{
    public int id;            // 아이디
    public string name;       // 이름
    public string hp;         // 체력
    public string weight;     // 무게
    public string damage;     // 데미지

    public int GetKey()
    {
        return id;
    }
}
