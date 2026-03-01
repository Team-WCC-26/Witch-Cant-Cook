using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

public class JudgePhase : PhaseBase
{
    public JudgePhase(StageManager owner) : base(owner) { }

    public override void OnEnter() => Debug.Log("<color=cyan><b>[결산]</b></color> 페이즈 진입  - 스페이스바를 누르면 다음 라운드");

    public override void OnUpdate()
    {
        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            owner.FinishStage();
        }
    }

    public override void OnExit() => Debug.Log("<color=cyan><b>[결산]</b></color> 페이즈 종료");

}
