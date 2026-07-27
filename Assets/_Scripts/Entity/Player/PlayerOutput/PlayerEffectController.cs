using System.Collections;
using UnityEngine;

public class PlayerEffectController : MonoBehaviour
{
    private PlayerBrain brain;

    private Coroutine movementCoroutine;

    private float originalSpeed;
    private float originalRunMultiplier;
    public struct MovementEffectData
    {
        public bool ChangeSpeed;
        public float SpeedMultiplier;

        public bool ChangeFriction;
        public float FrictionMultiplier;

        public float Duration;
    }
    private void Awake()
    {
        brain = GetComponent<PlayerBrain>();
    }

    /// <summary>
    /// ¿ÀÂ¡¾î ¸Ô¹° È¿°ú
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
    /// ¹ö¼¸ µîÀÇ ³Ë¹é È¿°ú
    /// </summary>
    public void ApplyKnockback(Vector3 force)
    {
        brain.Rb.AddForce(force, ForceMode.Impulse);
    }


    #region Movement
    /// <summary>
    /// ÀÌµ¿¼Óµµ °¨¼Ò
    /// </summary>
    public void ApplyMovementEffect(MovementEffectData data)
    {
        if (movementCoroutine != null)
            StopCoroutine(movementCoroutine);

        movementCoroutine = StartCoroutine(CoMovement(data));
    }

    private IEnumerator CoMovement(MovementEffectData data)
    {
        PlayerMovement movement =
            brain.ActionController.Movement;

        float prevMultiplier = movement.SpeedMultiplier;

        if (data.ChangeSpeed)
        {
            movement.SpeedMultiplier = data.SpeedMultiplier;
        }

        if (data.ChangeFriction)
        {
            // TODO : ¸¶Âû Àû¿ë
        }

        yield return new WaitForSeconds(data.Duration);

        if (data.ChangeSpeed)
        {
            movement.SpeedMultiplier = prevMultiplier;
        }

        if (data.ChangeFriction)
        {
            // TODO : ¸¶Âû º¹±¸
        }

        movementCoroutine = null;
    }

    #endregion
}


