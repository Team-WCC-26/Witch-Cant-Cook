using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

[Serializable]
public struct IngredientAttribute : IData, IComponentData

{
    public int id;            // 아이디
    public FixedString64Bytes name;       // 이름
    public FixedString64Bytes stat_id;    // 스탯 id
    public FixedString64Bytes throwing;   // 투척 형태
    public FixedString64Bytes tag;        // 태그 모음

    public int GetKey() => id;

}

public struct IngredientStat : IData, IComponentData
{
    public int id;            // 아이디
    public FixedString64Bytes name;       // 이름
    public int hp;         // 체력
    public float weight;     // 무게
    public int damage;     // 데미지

    public int GetKey() => id;
}