using UnityEngine;

public interface IConveyorSegment
{
    float Length { get; }
    Vector3 GetPoint(float distanceAlongSegment);
    Vector3 GetTangent(float distanceAlongSegment);
}