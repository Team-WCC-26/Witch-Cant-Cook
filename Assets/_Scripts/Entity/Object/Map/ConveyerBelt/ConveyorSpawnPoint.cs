using UnityEngine;

/// <summary>
/// 맵에 배치되는 재료 스폰 지점. DB의 beltID와 동일한 값을 인스펙터에서 입력해두면,
/// 서버가 보내는 스폰 요청의 beltID로 이 지점(및 대응하는 컨베이어 벨트)을 찾을 수 있다.
///
/// 이 클래스는 의도적으로 ConveyorBeltController를 전혀 참조하지 않는다.
/// "여기가 어디고 ID가 뭔지"만 알면 되고, 그 ID로 어떤 벨트를 찾을지는
/// ConveyorBeltRegistry의 책임으로 분리한다.
/// </summary>
public class ConveyorSpawnPoint : MonoBehaviour
{
    [SerializeField] private int beltId; // 서버 패킷의 ConveyId와 동일한 값

    public int BeltId => beltId;
    public Vector3 Position => transform.position;
    public Quaternion Rotation => transform.rotation;

    private void OnEnable()
    {
        ConveyorSpawnPointRegistry.Register(this);
    }

    private void OnDisable()
    {
        ConveyorSpawnPointRegistry.Unregister(this);
    }
}