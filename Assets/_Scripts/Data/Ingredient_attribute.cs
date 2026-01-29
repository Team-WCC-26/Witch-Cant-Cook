using System;
using UnityEngine;

[Serializable]
public class Ingredient_attribute : IData
{
    public int id;            // 아이디
    public string name;       // 이름
    public string stat_id;    // 스탯 id
    public string throwing;   // 투척 형태
    public string tag;        // 태그 모음

    public int GetKey()
    {
        return id;
    }
}