using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

[Serializable]
public struct IngredientAttribute : IData, ISerializationCallbackReceiver

{
    public int id;            // 아이디
    public FixedString64Bytes name;       // 이름
    public FixedString64Bytes stat_id;    // 스탯 id
    public FixedString64Bytes throwing;   // 투척 형태
    public FixedString64Bytes tag;        // 태그 모음 
    // 이거 리스트여야되는거 아님?
    // 시트에서는 "Fire, Spicy, Vegetable"처럼 콤마로 구분된 하나의 문자열로 받고,
    // 나중에 게임 로직에서 이를 해석하여 여러 개의 Tag Component(FireTag, SpicyTag)를 엔티티에 각각 붙여주기

    // -- [json 지원용 임시 필드]
    [SerializeField, HideInInspector] private string _name;
    [SerializeField, HideInInspector] private string _statID;
    [SerializeField, HideInInspector] private string _throwing;
    [SerializeField, HideInInspector] private string _tag;
    public int GetKey() => id;

    public void OnBeforeSerialize()
    {
        _name = name.ToString();
        _statID = stat_id.ToString();
        _throwing = throwing.ToString();
        _tag = tag.ToString();
    }

    public void OnAfterDeserialize()
    {
        name = _name;
        stat_id = _statID;
        throwing = _throwing;
        tag = _tag;
    }

}

public struct IngredientStat : IData, ISerializationCallbackReceiver
{
    public int id;            // 아이디
    public FixedString64Bytes name;       // 이름
    public int hp;         // 체력
    public float weight;     // 무게
    public int damage;     // 데미지

    [SerializeField, HideInInspector] private string _name;

    public int GetKey() => id;

    public void OnBeforeSerialize()
    {
        _name = name.ToString();
    }

    public void OnAfterDeserialize()
    {
        name = _name;
    }
}