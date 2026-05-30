using UnityEngine;
using Unity.Entities;
using Unity.Physics;
using Unity.Collections;

public class AutomaticTool : CookingToolBase
{
    public float cookInterval = 0.2f; // 0.2초마다 한 번씩만 물리 체크
    private float timer;

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= cookInterval)
        {
            ScanAndProcess();
            timer = 0f;
        }
    }

    protected override void OnIngredientDetected(Entity ingredient)
    {
        // 냄비 전용 로직: 천천히 익힘 (HP 감소 등)
        Debug.Log($"[냄비] {ingredient} 익히는 중...");
    }
}