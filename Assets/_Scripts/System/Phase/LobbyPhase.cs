using UnityEngine;

// 로비 시간
// 게임 처음 시작할 때, 게임 오버 시 전환되는 페이즈
public class LobbyPhase : PhaseBase
{
    public LobbyPhase(StageManager owner) : base(owner) { }

    public override void OnEnter() => Debug.Log("<color=green><b>[로비]</b></color> 페이즈 진입");

    public override void OnUpdate()
    {
        
    }

    public override void OnExit() => Debug.Log("<color=green><b>[로비]</b></color> 페이즈 종료");

}
