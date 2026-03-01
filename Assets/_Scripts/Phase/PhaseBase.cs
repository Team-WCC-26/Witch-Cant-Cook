using UnityEngine;

public abstract class PhaseBase
{
    protected StageManager owner;

    public PhaseBase(StageManager sm) 
    {
        this.owner = sm;
    }

    public abstract void OnEnter();   // 페이즈 진입 시 (초기화)
    public abstract void OnUpdate();  // 페이즈 진행 중 (매 프레임)
    public abstract void OnExit();    // 페이즈 종료 시 (정리)
}
