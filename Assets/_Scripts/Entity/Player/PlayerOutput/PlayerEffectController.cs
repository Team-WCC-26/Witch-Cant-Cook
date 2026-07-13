using UnityEngine;

public class PlayerEffectController
{
    private readonly PlayerBrain brain;


    public PlayerEffectController(PlayerBrain brain)
    {
        this.brain = brain;
    }

    /// <summary>
    /// 오징어 먹물 효과
    /// </summary>
    public void ApplyBlind(float duration)
    {
        _ = UIManager.Show<UIBlind>(duration);
    }

    /// <summary>
    /// 버섯 등의 넉백 효과
    /// </summary>
    public void ApplyKnockback(Vector3 force)
    {
        brain.Rb.AddForce(force, ForceMode.Impulse);
    }

    /// <summary>
    /// 이동속도 감소
    /// </summary>
    public void ApplySpeedDown(float multiplier, float duration)
    {
        // TODO
    }


    // - (양파) 마찰력 계수 낮추기

}


