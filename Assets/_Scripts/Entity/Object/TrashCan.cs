using Protocol;
using Unity.Transforms;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static UnityEditor.FilePathAttribute;

public class TrashCan : MonoBehaviour
{
    // entity 동기화 작업 서버쪽에서 해야 양쪽 다 보일거같은데

    private int playerLayer;
    private  int catchableLayer;

    [Header("Respawn Point")]
    public Transform kitchenSpawnPoint;

    private void Awake()
    {
        playerLayer = LayerMask.NameToLayer("Ragdoll");
        catchableLayer = LayerMask.NameToLayer("Catchable");
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.layer == playerLayer)
        {
            RespawnPlayer(other);
            return;
        }

        if (other.gameObject.layer != catchableLayer)
            return;

        if (!other.TryGetComponent(out CatchableObj catchable))
            return;

        switch (catchable.ObjType)
        {
            case CatchableObjType.Ingredient:
                HandleIngredient(catchable);
                break;

            default:
                HandleTool(catchable);
                break;
        }
    }
    void RespawnPlayer(Collider other)
    {
        if (other.TryGetComponent(out PlayerBrain player))
        {
            PlayerSpawnManager.Instance.RespawnPlayer(player.PlayerId, kitchenSpawnPoint);
        }
    }
    void HandleTool(CatchableObj catchable)
    {
        // 일단 임시, tool 관리 매니저 생성 시 내용 옮기기
        GameObject go = catchable.gameObject;
        go.transform.SetPositionAndRotation(
            kitchenSpawnPoint.position,
            kitchenSpawnPoint.rotation);

        if (go.TryGetComponent(out Rigidbody rb))
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

    }

    void HandleIngredient(CatchableObj catchable)
    {
        // push할 때 서버 연동 용으로 destroy했다는 패킷 보내야되는데
        //EntityDestroyPacket packet = new EntityDestroyPacket();
        ObjectPoolManager.Instance.Push(catchable.gameObject);
    }

}
