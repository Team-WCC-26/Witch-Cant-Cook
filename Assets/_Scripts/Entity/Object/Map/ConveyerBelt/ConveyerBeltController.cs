using UnityEngine;

public class ConveyorBeltController : MonoBehaviour
{
    [SerializeField] private ConveyorPath path;
    [SerializeField] private float beltSpeed = 2f;
    [SerializeField] private Renderer beltRenderer;

    public ConveyorPath Path => path;
    public float Speed => beltSpeed;

    void Update()
    {
        // UV 스크롤은 순수 로컬 비주얼, 동기화 불필요
        float offset = Time.time * beltSpeed * 0.1f;
        beltRenderer.material.SetTextureOffset("_MainTex", new Vector2(0, offset));
    }
}