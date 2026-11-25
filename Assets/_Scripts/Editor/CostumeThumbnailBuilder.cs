using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class CostumeThumbnailBuilder
{
    private const string OutputFolder = "Assets/Addressables/Textures/CostumeThumbnail";
    private const string AddressPrefix = "CostumeThumbnail/";
    private const string GroupName = "Costume Thumbnails";
    private const int Width = 256;
    private const int Height = 186;
    private const int PreviewLayer = 5;
    private static readonly string[] Parts =
    {
        "Hair", "Hat", "Mustache", "Eyebrow", "Glasses", "Body",
        "Glove", "Backpack", "Outerwear", "Pants", "Shoe", "Full"
    };

    [Serializable]
    private sealed class BodyPalette
    {
        public Color[] colors;
    }

    /// <summary>Regenerates all thumbnails and the body palette from the appearance catalog.</summary>
    [MenuItem("Tools/Player/Rebuild Costume Thumbnails")]
    public static void Rebuild()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before capturing costume thumbnails.");
        PlayerAppearanceCatalog.GetEditorCatalog();
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) throw new InvalidOperationException("Addressables settings are missing.");

        var images = new Dictionary<string, byte[]>();
        var palette = new BodyPalette { colors = new Color[PlayerAppearanceCatalog.VariantCount("Body")] };
        using (var stage = new CaptureStage())
        {
            foreach (string part in Parts)
            {
                int count = PlayerAppearanceCatalog.VariantCount(part);
                if (count == 0) throw new InvalidOperationException($"No variants found for {part}.");
                for (int slot = 1; slot <= count; slot++)
                {
                    var variant = PlayerAppearanceCatalog.GetVariant(part, slot);
                    RectInt crop = stage.Capture(variant, part);
                    if (crop.width == 0) throw new InvalidOperationException($"Empty thumbnail: {part}/{slot}.");
                    if (part == "Body")
                        palette.colors[slot - 1] = stage.SampleBody(crop);
                    else
                        images.Add($"{OutputFolder}/{part}/{slot}.png", stage.EncodeThumbnail(crop));
                }
            }
        }

        string palettePath = $"{OutputFolder}/BodyPalette.json";
        foreach (var image in images)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(image.Key));
            File.WriteAllBytes(image.Key, image.Value);
        }
        File.WriteAllText(palettePath, JsonUtility.ToJson(palette, true));
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        var group = settings.FindGroup(GroupName) ?? settings.CreateGroup(GroupName, false, false, false, null,
            typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        foreach (string part in Parts)
        {
            string folder = $"{OutputFolder}/{part}";
            if (!Directory.Exists(folder)) continue;
            foreach (string file in Directory.GetFiles(folder, "*.png"))
            {
                string path = file.Replace(Path.DirectorySeparatorChar, '/');
                if (!int.TryParse(Path.GetFileNameWithoutExtension(path), out _) || images.ContainsKey(path)) continue;
                settings.RemoveAssetEntry(AssetDatabase.AssetPathToGUID(path), false);
                AssetDatabase.DeleteAsset(path);
            }
        }
        foreach (string path in images.Keys)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (!importer.alphaIsTransparency || importer.mipmapEnabled)
            {
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            Register(settings, group, path, path.Substring(OutputFolder.Length + 1, path.Length - OutputFolder.Length - 5));
        }
        Register(settings, group, palettePath, "BodyPalette");
        EditorUtility.SetDirty(group);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Debug.Log($"Generated {images.Count} costume thumbnails and {palette.colors.Length} body colors.");
    }

    private static void Register(AddressableAssetSettings settings, AddressableAssetGroup group, string path, string key)
    {
        var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), group, false, false);
        entry.address = AddressPrefix + key;
    }

    private sealed class CaptureStage : IDisposable
    {
        private readonly GameObject root;
        private readonly Camera cam;
        private readonly MeshFilter mesh;
        private readonly MeshRenderer renderer;
        private readonly RenderTexture target;
        private readonly Texture2D readback;
        private readonly Texture2D thumbnail;
        private readonly Color32[] thumbnailPixels = new Color32[Width * Height];

        internal CaptureStage()
        {
            root = new GameObject("Costume Thumbnail Capture") { hideFlags = HideFlags.HideAndDontSave };
            root.transform.position = Vector3.up * 10000f;

            var meshObj = new GameObject("Part");
            meshObj.layer = PreviewLayer;
            meshObj.transform.SetParent(root.transform, false);
            mesh = meshObj.AddComponent<MeshFilter>();
            renderer = meshObj.AddComponent<MeshRenderer>();

            var camObj = new GameObject("Camera");
            camObj.transform.SetParent(root.transform, false);
            cam = camObj.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.clear;
            cam.cullingMask = 1 << PreviewLayer;
            cam.orthographic = true;
            cam.aspect = (float)Width / Height;
            cam.farClipPlane = 12f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = false;
            camData.renderShadows = false;

            var lightObj = new GameObject("Light");
            lightObj.transform.SetParent(root.transform, false);
            lightObj.transform.localPosition = new Vector3(1f, 2f, 2f);
            Light light = lightObj.AddComponent<Light>();
            light.type = LightType.Point;
            light.cullingMask = 1 << PreviewLayer;
            light.range = 10f;
            light.intensity = 4f;

            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            readback = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            thumbnail = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        }

        internal RectInt Capture(PlayerAppearanceCatalog.Variant variant, string part)
        {
            Bounds bounds = variant.mesh.bounds;
            Quaternion view = part switch
            {
                "Glove" => Quaternion.Euler(0f, 180f, 0f),
                "Pants" => Quaternion.LookRotation(new Vector3(-0.55f, 0.25f, -3f)),
                _ => Quaternion.LookRotation(new Vector3(-1.5f, -0.7f, -3f))
            };

            if (part == "Glove")
            {
                Vector3 center = bounds.center + Vector3.right * (bounds.extents.x * 0.76f);
                bounds = new Bounds(center, new Vector3(bounds.size.x * 0.24f, bounds.size.y, bounds.size.z));
            }

            float halfWidth = ProjectExtents(bounds.extents, view * Vector3.right);
            float halfHeight = ProjectExtents(bounds.extents, view * Vector3.up);
            float scale = 0.5f / Mathf.Max(0.001f, Mathf.Max(halfWidth / cam.aspect, halfHeight));
            mesh.sharedMesh = variant.mesh;
            renderer.sharedMaterials = variant.materials;
            mesh.transform.localScale = Vector3.one * scale;
            mesh.transform.localPosition = -bounds.center * scale;
            cam.orthographicSize = part == "Glove" ? 0.68f : 0.56f;
            cam.transform.localPosition = -(view * Vector3.forward) * 3f;
            cam.transform.localRotation = view;

            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(cam, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            }
            finally
            {
                RenderTexture.active = previous;
            }

            var pixels = readback.GetRawTextureData<Color32>();
            int minX = Width, minY = Height, maxX = -1, maxY = -1;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                if (pixels[y * Width + x].a < 24) continue;
                minX = Mathf.Min(minX, x);
                minY = Mathf.Min(minY, y);
                maxX = Mathf.Max(maxX, x);
                maxY = Mathf.Max(maxY, y);
            }
            return maxX < minX ? default : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        internal Color SampleBody(RectInt crop)
        {
            Color color = readback.GetPixel(crop.x + crop.width / 2, crop.y + (int)(crop.height * 0.61f));
            return color.a > 0.1f ? new Color(color.r, color.g, color.b) : Color.gray;
        }

        internal byte[] EncodeThumbnail(RectInt crop)
        {
            Array.Clear(thumbnailPixels, 0, thumbnailPixels.Length);
            float scale = Mathf.Min((Width - 12f) / crop.width, (Height - 12f) / crop.height);
            int width = Mathf.Max(1, Mathf.RoundToInt(crop.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(crop.height * scale));
            int offsetX = (Width - width) / 2;
            int offsetY = (Height - height) / 2;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float u = (crop.x + (x + 0.5f) * crop.width / width) / Width;
                float v = (crop.y + (y + 0.5f) * crop.height / height) / Height;
                thumbnailPixels[(offsetY + y) * Width + offsetX + x] = readback.GetPixelBilinear(u, v);
            }
            thumbnail.SetPixels32(thumbnailPixels);
            return thumbnail.EncodeToPNG();
        }

        private static float ProjectExtents(Vector3 extents, Vector3 axis) =>
            Mathf.Abs(axis.x) * extents.x + Mathf.Abs(axis.y) * extents.y + Mathf.Abs(axis.z) * extents.z;

        /// <summary>Releases the capture stage and temporary textures.</summary>
        public void Dispose()
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(readback);
            UnityEngine.Object.DestroyImmediate(thumbnail);
        }
    }
}
