using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;
using NQuaternion = System.Numerics.Quaternion;

using UVector2 = UnityEngine.Vector2;
using UVector3 = UnityEngine.Vector3;
using UVector4 = UnityEngine.Vector4;
using UQuaternion = UnityEngine.Quaternion;

public static class DataConverter
{
    public static NVector2 UnityToNumerics(UVector2 value) => new(value.x, value.y);
    public static NVector3 UnityToNumerics(UVector3 value) => new(value.x, value.y, value.z);
    public static NVector4 UnityToNumerics(UVector4 value) => new(value.x, value.y, value.z, value.w);
    public static NQuaternion UnityToNumerics(UQuaternion value) => new(value.x, value.y, value.z, value.w);

    public static UVector2 NumericsToUnity(NVector2 value) => new(value.X, value.Y);
    public static UVector3 NumericsToUnity(NVector3 value) => new(value.X, value.Y, value.Z);
    public static UVector4 NumericsToUnity(NVector4 value) => new(value.X, value.Y, value.Z, value.W);
    public static UQuaternion NumericsToUnity(NQuaternion value) => new(value.X, value.Y, value.Z, value.W);
}
