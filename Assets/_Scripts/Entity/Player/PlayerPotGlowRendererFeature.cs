using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public sealed class PlayerPotGlowRendererFeature : ScriptableRendererFeature
{
    [SerializeField] private Shader glowShader;
    [SerializeField] private Color glowColor = new(0.3f, 0.9f, 1f, 0.9f);
    [SerializeField, Min(1f)] private float outlineWidth = 2f;
    [SerializeField, Min(1f)] private float glowWidth = 8f;
    private Material material;
    private GlowPass pass;

    #region Unity Callbacks
    public override void Create()
    {
        CoreUtils.Destroy(material);
        if (glowShader == null) return;
        material = CoreUtils.CreateEngineMaterial(glowShader);
        pass = new GlowPass(material) { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (pass == null || material == null || PlayerGlow.ActivePlayers.Count == 0 ||
            renderingData.cameraData.cameraType != CameraType.Game) return;
        material.SetColor("_GlowColor", glowColor);
        material.SetFloat("_OutlineWidth", outlineWidth);
        material.SetFloat("_GlowWidth", Mathf.Max(outlineWidth, glowWidth));
        renderer.EnqueuePass(pass);
    }

    protected override void Dispose(bool disposing) => CoreUtils.Destroy(material);
    #endregion

    private sealed class GlowPass : ScriptableRenderPass
    {
        private static readonly int maskId = Shader.PropertyToID("_PlayerPotGlowMask");
        private readonly Material material;
        private readonly Plane[] frustumPlanes = new Plane[6];
        private readonly List<Renderer> visibleRenderers = new();
        private sealed class PassData
        {
            public Material material;
            public Renderer[] renderers;
        }

        public GlowPass(Material material) => this.material = material;

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalCameraData camera = frameData.Get<UniversalCameraData>();
            GeometryUtility.CalculateFrustumPlanes(camera.camera, frustumPlanes);
            visibleRenderers.Clear();
            foreach (PlayerGlow player in PlayerGlow.ActivePlayers)
            {
                if (player == null || player.Renderers == null) continue;
                foreach (Renderer body in player.Renderers)
                {
                    if (body == null || !body.enabled || !body.gameObject.activeInHierarchy ||
                        (camera.camera.cullingMask & (1 << body.gameObject.layer)) == 0 ||
                        !GeometryUtility.TestPlanesAABB(frustumPlanes, body.bounds)) continue;
                    visibleRenderers.Add(body);
                }
            }
            if (visibleRenderers.Count == 0) return;

            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            var descriptor = camera.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            descriptor.graphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R8_UNorm;
            material.SetVector("_PlayerPotGlowMask_TexelSize", new Vector4(
                1f / descriptor.width, 1f / descriptor.height, descriptor.width, descriptor.height));
            TextureHandle mask = UniversalRenderer.CreateRenderGraphTexture(renderGraph, descriptor,
                "Player Pot Glow Mask", true);

            // Draw every body part into one mask, ignoring the pot's depth.
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Player Pot Glow Mask", out var data))
            {
                data.material = material;
                data.renderers = visibleRenderers.ToArray();
                builder.SetRenderAttachment(mask, 0, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(mask, maskId);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    foreach (Renderer body in data.renderers)
                    {
                        if (body == null) continue;
                        Mesh mesh = body is SkinnedMeshRenderer skin ? skin.sharedMesh :
                            body.GetComponent<MeshFilter>()?.sharedMesh;
                        if (mesh == null) continue;
                        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                            context.cmd.DrawRenderer(body, data.material, submesh, 0);
                    }
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Player Pot Glow Outline", out var data))
            {
                data.material = material;
                builder.UseTexture(mask, AccessFlags.Read);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 1, MeshTopology.Triangles, 3));
            }
        }
    }
}