using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerGlow : MonoBehaviour
{
    internal Renderer[] Renderers { get; private set; }

    private static readonly HashSet<PlayerGlow> activePlayers = new();
    internal static IReadOnlyCollection<PlayerGlow> ActivePlayers => activePlayers;

    private bool isOcclusionAllowed;

    #region Unity Callbacks

    private void Awake()
    {
        RefreshRenderers();
    }

    private void OnEnable()
    {
        DisableOcclusion();
        activePlayers.Add(this);
    }

    private void OnDisable()
    {
        // 글로우 대상으로 등록됐던 경우에만 오클루전 설정 복원
        if (activePlayers.Remove(this)) RestoreOcclusion();
    }

    #endregion

    public static void SetActive(PlayerBrain player, bool active)
    {
        if (player == null || player.Glow == null) return;
        player.Glow.enabled = active;
    }

    public void RefreshRenderers()
    {
        bool isGlowActive = activePlayers.Contains(this);
        if (isGlowActive) RestoreOcclusion();

        var renderers = new List<Renderer>();
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer) continue;
            if (renderer.GetComponentInParent<CatchableObj>() != null) continue;
            renderers.Add(renderer);
        }
        Renderers = renderers.ToArray();

        if (isGlowActive) DisableOcclusion();
    }

    private void DisableOcclusion()
    {
        if (Renderers == null) return;

        foreach (Renderer renderer in Renderers)
        {
            if (renderer == null) continue;
            isOcclusionAllowed = renderer.allowOcclusionWhenDynamic;
            break;
        }

        foreach (Renderer renderer in Renderers)
            if (renderer != null) renderer.allowOcclusionWhenDynamic = false;
    }

    private void RestoreOcclusion()
    {
        if (Renderers == null) return;

        foreach (Renderer renderer in Renderers)
            if (renderer != null) renderer.allowOcclusionWhenDynamic = isOcclusionAllowed;
    }   
}