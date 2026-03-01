using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "StageData", menuName = "Scriptable Objects/StageData", order = 1)]
public class StageData : ScriptableObject
{
    [Header("Phase Duration")]

    [Tooltip("준비 시간을 입력해주세요. (단위: 분)")]
    public float prepDuration;
    [Tooltip("조리 시간을 입력해주세요. (단위: 분)")]
    public float cookingDuration;

    [Header("Goal & Info")]

    [Tooltip("스테이지 번호를 입력해주세요.")]
    public int stageNumber;
    [Tooltip("(임시)스테이지에서 등장할 레시피 인덱스를 넣어주세요.")]
    public List<int> recipeList;

}
