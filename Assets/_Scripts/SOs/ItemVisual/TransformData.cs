using UnityEngine;

[CreateAssetMenu(fileName = "NewTransform", menuName = "Scriptable Objects/Transform")]
public class TransformData : ScriptableObject
{
    [SerializeField] private Vector3 positionOffset;
    [SerializeField] private Vector3 rotationOffset;
    [SerializeField] private Vector3 scale;

    public Vector3 PositionOffset => positionOffset;
    public Vector3 RotationOffset => rotationOffset;
    public Vector3 Scale => scale;
}