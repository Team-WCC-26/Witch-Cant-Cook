using UnityEngine;

public class IngredientTraitAreaCreator : MonoBehaviour
{
    [Header("Area")]

    // 테이블 생기면 테스트 해보기 
    [SerializeField] private string areaPoolKey;

    /// <summary>
    /// 영역 생성
    /// </summary>
    public GameObject CreateArea()
    {
        GameObject area = ObjectPoolManager.Instance.Pop(areaPoolKey);

        if (area == null)
            return null;

        area.transform.SetPositionAndRotation(
            transform.position,
            Quaternion.identity);

        return area;
    }

    public GameObject CreateArea(Vector3 position)
    {
        GameObject area = ObjectPoolManager.Instance.Pop(areaPoolKey);

        if (area == null)
            return null;

        area.transform.SetPositionAndRotation(
            position,
            Quaternion.identity);

        return area;
    }
    // IngredientSpawnSystem 거쳐서 영역 생성하도록 
}
