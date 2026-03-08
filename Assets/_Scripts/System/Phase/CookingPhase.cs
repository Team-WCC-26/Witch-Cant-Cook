using UnityEngine;
using static UnityEngine.UI.GridLayoutGroup;

public class CookingPhase : PhaseBase
{
    public CookingPhase(StageManager owner) : base(owner) { }

    private float timer;
    public override void OnEnter()
    {
        timer = owner.CurrentStageData.cookingDuration * 60f;
        Debug.Log("<color=yellow><b>[조리]</b></color> 페이즈 진입");
    }

    public override void OnUpdate()
    {
        timer -= Time.deltaTime;
        if (timer <= 0) owner.ChangePhase(new JudgePhase(owner));
    }

    public override void OnExit() => Debug.Log("<color=yellow><b>[조리]</b></color> 페이즈 종료");
}
