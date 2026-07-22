using UnityEngine;

public class PlayerEffectController : MonoBehaviour
{
    private PlayerBrain brain;

    private void Awake()
    {
        brain = GetComponent<PlayerBrain>();
    }

    /// <summary>
    /// 오징어 먹물 효과
    /// </summary>
    public void ApplyBlind(float duration)
    {
        Debug.Log($"brain : {brain}");
        Debug.Log($"spawnManager : {PlayerSpawnManager.Instance}");
        Debug.Log($"playerId : {brain?.PlayerId}");

        if (!PlayerSpawnManager.Instance.IsMine(brain.PlayerId))
            return;

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
    public void ApplySpeedDown(float multiplier)
    {
        // 범위 체크는 HoneyArea가 할거고
        // 이 범위 안에 있는 플레이어의 속도 조절
        // TODO
    }


    // - (양파) 마찰력 계수 낮추기

}


