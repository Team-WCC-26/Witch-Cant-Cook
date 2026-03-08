using UnityEngine;

public class PrepPhase : PhaseBase
{
    public PrepPhase(StageManager owner) : base(owner) { }

    private float timer;
    public override void OnEnter()
    {
        timer = owner.CurrentStageData.prepDuration * 60f;
        Debug.Log("<color=orange><b>[준비]</b></color> 페이즈 진입");
    }

    public override void OnUpdate()
    {
        // 시간 무제한으로 변경 가능성 있음
        timer -= Time.deltaTime;
        if (timer <= 0) owner.ChangePhase(new CookingPhase(owner));
    }

    public override void OnExit() => Debug.Log("<color=orange><b>[준비]</b></color> 페이즈 종료");

}
