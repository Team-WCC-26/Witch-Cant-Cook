using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ConveyorPath 전용 커스텀 에디터.
/// - 씬 뷰에서 waypoint를 직접 드래그 (색으로 시작/코너/끝 구분)
/// - 진행 방향을 화살표로 시각화
/// - 인스펙터 버튼으로 waypoint 추가/삽입/이름 재정렬
/// - 총 길이 / waypoint 개수 표시
/// </summary>
[CustomEditor(typeof(ConveyorPath))]
public class ConveyorPathEditor : Editor
{
    private ConveyorPath path;

    private void OnEnable()
    {
        path = (ConveyorPath)target;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("경로 정보", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"총 길이: {path.TotalLength:F2} m");
        EditorGUILayout.LabelField($"Waypoint 개수: {path.WaypointCount}");

        EditorGUILayout.Space();
        if (GUILayout.Button("끝에 Waypoint 추가"))
        {
            AddWaypointAtEnd();
        }

        using (new EditorGUI.DisabledScope(Selection.activeTransform == null ||
               System.Array.IndexOf(path.Waypoints ?? new Transform[0], Selection.activeTransform) < 0))
        {
            if (GUILayout.Button("선택된 Waypoint 뒤에 삽입"))
            {
                InsertWaypointAfterSelected();
            }
        }

        if (GUILayout.Button("모든 Waypoint 이름 재정렬 (Waypoint_0, 1, 2 ...)"))
        {
            RenameWaypointsInOrder();
        }

        if (GUI.changed)
        {
            path.Build();
            SceneView.RepaintAll();
        }
    }

    private void OnSceneGUI()
    {
        DrawWaypointHandles();
        DrawDirectionArrows();
    }

    // ---- 씬 뷰 핸들 ----

    private void DrawWaypointHandles()
    {
        var waypoints = path.Waypoints;
        if (waypoints == null) return;

        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] == null) continue;

            EditorGUI.BeginChangeCheck();

            Handles.color = GetWaypointColor(i, waypoints.Length);
            float handleSize = HandleUtility.GetHandleSize(waypoints[i].position) * 0.15f;
            Handles.SphereHandleCap(0, waypoints[i].position, Quaternion.identity, handleSize, EventType.Repaint);
            Vector3 newPos = Handles.PositionHandle(waypoints[i].position, Quaternion.identity);

            Handles.Label(waypoints[i].position + Vector3.up * 0.5f, $"[{i}] {waypoints[i].name}");

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(waypoints[i], "Move Conveyor Waypoint");
                waypoints[i].position = newPos;
                path.Build();
                EditorUtility.SetDirty(path);
            }
        }
    }

    private Color GetWaypointColor(int index, int total)
    {
        if (index == 0) return Color.green;           // 시작
        if (index == total - 1) return Color.red;      // 끝
        return Color.yellow;                            // 코너
    }

    private void DrawDirectionArrows()
    {
        if (path.TotalLength <= 0f) return;

        Handles.color = Color.cyan;
        int arrowCount = 10;
        for (int i = 0; i < arrowCount; i++)
        {
            float d = path.TotalLength * i / arrowCount;
            var (point, tangent) = path.Evaluate(d);
            if (tangent.sqrMagnitude < 0.0001f) continue;
            Handles.ArrowHandleCap(0, point, Quaternion.LookRotation(tangent), 0.5f, EventType.Repaint);
        }
    }

    // ---- Waypoint 추가/삽입 ----

    private void AddWaypointAtEnd()
    {
        Undo.RecordObject(path, "Add Waypoint");

        var waypoints = new List<Transform>(path.Waypoints ?? new Transform[0]);

        GameObject wpObj = new GameObject($"Waypoint_{waypoints.Count}");
        Undo.RegisterCreatedObjectUndo(wpObj, "Create Waypoint");
        wpObj.transform.SetParent(path.transform);

        if (waypoints.Count > 0 && waypoints[^1] != null)
            wpObj.transform.position = waypoints[^1].position + path.transform.forward * 2f;
        else
            wpObj.transform.position = path.transform.position;

        waypoints.Add(wpObj.transform);
        path.SetWaypoints(waypoints.ToArray());

        EditorUtility.SetDirty(path);
        Selection.activeGameObject = wpObj;
    }

    private void InsertWaypointAfterSelected()
    {
        var waypoints = new List<Transform>(path.Waypoints ?? new Transform[0]);
        int selectedIndex = waypoints.IndexOf(Selection.activeTransform);

        if (selectedIndex < 0)
        {
            Debug.LogWarning("[ConveyorPathEditor] 삽입하려면 기존 waypoint를 하나 선택하세요.");
            return;
        }

        Undo.RecordObject(path, "Insert Waypoint");

        GameObject wpObj = new GameObject("Waypoint_new");
        Undo.RegisterCreatedObjectUndo(wpObj, "Create Waypoint");
        wpObj.transform.SetParent(path.transform);

        Transform current = waypoints[selectedIndex];
        Transform next = selectedIndex + 1 < waypoints.Count ? waypoints[selectedIndex + 1] : null;
        wpObj.transform.position = next != null
            ? Vector3.Lerp(current.position, next.position, 0.5f)
            : current.position + path.transform.forward * 2f;

        waypoints.Insert(selectedIndex + 1, wpObj.transform);
        path.SetWaypoints(waypoints.ToArray());
        RenameWaypointsInOrder();

        EditorUtility.SetDirty(path);
        Selection.activeGameObject = wpObj;
    }

    private void RenameWaypointsInOrder()
    {
        var waypoints = path.Waypoints;
        if (waypoints == null) return;

        Undo.RecordObjects(waypoints, "Rename Waypoints");
        for (int i = 0; i < waypoints.Length; i++)
        {
            if (waypoints[i] != null)
                waypoints[i].name = $"Waypoint_{i}";
        }
    }
}