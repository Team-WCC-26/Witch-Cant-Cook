using UnityEngine;

public class IngredientTraitTracker : MonoBehaviour
{
    [Header("Tracking")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private float searchRadius = 20f;
    [SerializeField] private LayerMask playerLayer;

    private Rigidbody rb;
    private PlayerBrain target;
    private bool isTracking;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    public void StartTracking()
    {
        //target = FindNearestPlayer();  -> 서버가 정해주는 것으로 받기..

        if (target == null)
        {
            isTracking = false;
            return;
        }

        isTracking = true;
    }

    public void StopTracking()
    {
        isTracking = false;
        target = null;

        if (rb != null)
            rb.linearVelocity = Vector3.zero;
    }

    private void FixedUpdate()
    {
        if (!isTracking || target == null)
            return;

        Vector3 dir = target.transform.position - transform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.01f)
        {
            rb.linearVelocity = Vector3.zero;
            return;
        }

        rb.linearVelocity = dir.normalized * moveSpeed;
    }
}
