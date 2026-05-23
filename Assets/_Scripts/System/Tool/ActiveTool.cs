using Unity.Entities;
using UnityEngine;

public class ActiveTool : CookingToolBase
{
    // Update는 비워두거나 아예 쓰지 않음 (성능 이득)

    public void PerformAction()
    {
        // 클릭이나 휘두르는 애니메이션 특정 시점에 호출
        ScanAndProcess();
    }

    protected override void OnIngredientDetected(Entity ingredient)
    {
        // 칼 전용 로직: 한 번에 큰 데미지 혹은 즉시 절단
        Debug.Log($"[칼] {ingredient} 썰기!");
    }
}