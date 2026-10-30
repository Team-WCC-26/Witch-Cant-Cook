using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CatchableObj))]
public class IngredientDamageController : MonoBehaviour
{
    private static readonly float DamageThresholdRatio = 0.3f;
    public static float MaxThrowSpeed { get; private set; } = 0;
    private static float MinDamageSpeed => MaxThrowSpeed * DamageThresholdRatio;

    [SerializeField] private CatchableObj catchable;
    [SerializeField] private string playerTag = "Player";

    private float impactSpeed;
    private IngredientStat stat;
    private bool canDamage = true;
    private Coroutine damageRestoreCoroutine;
    private int damage => stat.damage;

    #region Unity Callbacks
    private void FixedUpdate()
    {
        impactSpeed = catchable.LinearVelocity.magnitude;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (catchable.TryGetComponent(out IngredientTraitBounce bounce) && bounce.IsSimulating) return;
        if (!canDamage) return;
        if (!collision.gameObject.CompareTag(playerTag)) return;
        PlayerBrain brain = collision.gameObject.GetComponentInParent<PlayerBrain>();
        if (brain == null || brain.StateResolver is not LocalPlayerStateResolver) return;
        if (stat == null) SetIngredinetStat();
        if (stat == null) return;
        if (MaxThrowSpeed == 0) MaxThrowSpeed = brain.ThrowForce;

        brain.Health.TakeDamage(CalculateDamage());
    }

    private void OnDisable()
    {
        if (damageRestoreCoroutine != null)
        {
            StopCoroutine(damageRestoreCoroutine);
            damageRestoreCoroutine = null;
        }

        canDamage = true;
    }
    #endregion

    public void ApplyScriptedImpact(PlayerBrain brain, float speed)
    {
        if (!canDamage || brain == null || brain.StateResolver is not LocalPlayerStateResolver) return;
        if (stat == null) SetIngredinetStat();
        if (stat == null) return;
        if (MaxThrowSpeed == 0) MaxThrowSpeed = brain.ThrowForce;
        impactSpeed = speed;
        brain.Health.TakeDamage(CalculateDamage());
    }

    // 지정된 시간 동안 재료의 충돌 피해를 비활성화한다.
    public void DisableDamageFor(float duration)
    {
        if (damageRestoreCoroutine != null)
            StopCoroutine(damageRestoreCoroutine);

        canDamage = false;
        damageRestoreCoroutine = StartCoroutine(RestoreDamageRoutine(duration));
    }

    // 대기 시간이 지나면 재료의 충돌 피해를 다시 활성화한다.
    private IEnumerator RestoreDamageRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);
        canDamage = true;
        damageRestoreCoroutine = null;
    }

    private void SetIngredinetStat()
    {
        if (catchable.Data is not Ingredient ingredient) return;
        stat = DataManager.Instance.GetIngredientStat().GetData(ingredient.statID);
    }

    private int CalculateDamage()
    {
        if (impactSpeed <= MinDamageSpeed) return 0;
        return Mathf.RoundToInt(damage * impactSpeed / MaxThrowSpeed);
    }
}
