using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class SheetInfo
{
    public string className;
    public string sheetId;

    [NonSerialized] public List<Dictionary<string, string>> datas;
}

public static class GSpreadExtensions
{
    public static TaskAwaiter<UnityWebRequest.Result> GetAwaiter(this UnityWebRequestAsyncOperation reqOp)
    {
        var tcs = new TaskCompletionSource<UnityWebRequest.Result>();
        reqOp.completed += _ => tcs.TrySetResult(reqOp.webRequest.result);

        if (reqOp.isDone)
            tcs.TrySetResult(reqOp.webRequest.result);

        return tcs.Task.GetAwaiter();
    }

    public static Vector3 ToVector3(this string str)
    {
        if (string.IsNullOrEmpty(str)) return Vector3.zero;

        str = str.Trim();
        if (str.StartsWith("(") && str.EndsWith(")"))
        {
            var pos = str.Substring(1, str.Length - 2).Split(',');
            if (pos.Length >= 3 &&
                float.TryParse(pos[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(pos[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                float.TryParse(pos[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                return new Vector3(x, y, z);
            }
        }
        return Vector3.zero;
    }

    public static Vector2 ToVector2(this string str)
    {
        if (string.IsNullOrEmpty(str)) return Vector2.zero;

        str = str.Trim();
        if (str.StartsWith("(") && str.EndsWith(")"))
        {
            var pos = str.Substring(1, str.Length - 2).Split(',');
            if (pos.Length >= 2 &&
                float.TryParse(pos[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(pos[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                return new Vector2(x, y);
            }
        }
        return Vector2.zero;
    }
}
