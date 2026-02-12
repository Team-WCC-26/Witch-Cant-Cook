using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace CharacterCustomization
{
    public class CharacterCustomizationWindow : EditorWindow
    {
        private readonly List<List<SavedPart>> _savedCombinations = new List<List<SavedPart>>();
        private readonly List<PartType> _partsOrder = new List<PartType>()
        {
            PartType.Hair,
            PartType.Glasses,
            PartType.Outerwear,
            PartType.Hat,
            PartType.Body,
            PartType.Pants,
            PartType.Mustache,
            PartType.Glove,
            PartType.Shoe,
            PartType.Eyebrow,
            PartType.Backpack,
            PartType.Full,
        };

        private PartsEditor _partsEditor;
        private Transform _cameraPivot;
        private Camera _camera;
        private RenderTexture _renderTexture;
        private List<Part> _parts;
        private Material _material;
        private Material _glassMaterial;
        private Material _emissionMaterial;
        private string _prefabPath;

        private Material Material
        {
            get
            {
                _material = _material ? _material : AssetDatabase.LoadAssetAtPath<Material>(AssetsPath.MainMaterial);

                return _material;
            }
        }

        private Material GlassMaterial
        {
            get
            {
                _glassMaterial = _glassMaterial ? _glassMaterial : AssetDatabase.LoadAssetAtPath<Material>(AssetsPath.GlassMaterial);

                return _glassMaterial;
            }
        }

        private Material EmissionMaterial
        {
            get
            {
                _emissionMaterial = _emissionMaterial ? _emissionMaterial : AssetDatabase.LoadAssetAtPath<Material>(AssetsPath.EmissionMaterial);

                return _emissionMaterial;
            }
        }

        private IEnumerable<Part> Parts => _parts ??= LoadParts().ToList();

        [MenuItem("Tools/Character Customization")]
        private static void Init()
        {
            FindRoot();
            var window = GetWindow<CharacterCustomizationWindow>("Character Customization");
            window.minSize = new Vector2(975, 720);
            window.Show();
        }

        private void OnEnable()
        {
            _partsEditor = new PartsEditor();
        }

        private void OnGUI()
        {
            var rect = new Rect(10, 10, 300, 300);

            CreateRenderTexture();
            InitializeCamera();
            DrawMesh();
            _partsEditor.OnGUI(new Rect(320, 10, position.width - 330, position.height), Parts);

            GUI.DrawTexture(rect, _renderTexture, ScaleMode.StretchToFill, false);

            GUI.Label(new Rect(10, 320, 100, 25), "Prefab folder:");
            GUI.Label(new Rect(10, 345, 350, 25), AssetsPath.SavedCharacters);
            _prefabPath = GUI.TextField(new Rect(10, 372, 300, 20), _prefabPath);

            var saveButtonRect = new Rect(10, 400, 300, 40);
            if (GUI.Button(saveButtonRect, "Save Prefab"))
            {
                SavePrefab();
            }

            var randomizeButtonRect = new Rect(85, 450, 150, 30);
            if (GUI.Button(randomizeButtonRect, "Randomize"))
            {
                Randomize();
            }

            var isZero = _savedCombinations.Count == 0;
            var isSame = false;
            var lessThenTwo = false;

            if (!isZero)
            {
                isSame = IsSame();
                lessThenTwo = _savedCombinations.Count < 2;
            }

            using (new EditorGUI.DisabledScope(isZero || (isSame && lessThenTwo)))
            {
                var lastButtonRect = new Rect(240, 450, 50, 30);
                if (GUI.Button(lastButtonRect, "Last"))
                {
                    Last();
                }
            }
        }

        private void SavePrefab()
        {
            var characterFbx = AssetDatabase.LoadAssetAtPath<GameObject>(AssetsPath.Fbx);
            var character = Instantiate(characterFbx, Vector3.zero, Quaternion.identity);
            foreach (Transform child in character.transform)
            {
                if (child.TryGetComponent<SkinnedMeshRenderer>(out var meshRenderer))
                {
                    var childName = child.name.Split('_').First();
                    var part = _parts.First(part => childName == part.Type.ToString());
                    meshRenderer.sharedMesh = part.IsEnabled ? part.SelectedVariant.Mesh : null;
                    if (meshRenderer.sharedMesh)
                    {
                        meshRenderer.sharedMaterials = part.SelectedVariant.Materials;
                    }
                }
            }

            AddAnimator(character);

            var prefabPath = AssetsPath.SavedCharacters + _prefabPath;
            Directory.CreateDirectory(prefabPath);
            var path = AssetDatabase.GenerateUniqueAssetPath($"{prefabPath}/Character.prefab");
            PrefabUtility.SaveAsPrefabAsset(character, path);
            DestroyImmediate(character);
        }

        private static void AddAnimator(GameObject character)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetsPath.AnimationController);
            var characterAnimator = character.GetComponent<Animator>();
            characterAnimator.runtimeAnimatorController = controller;
        }

        private async void Randomize()
        {
            foreach (var part in _parts)
            {
                if (Random.value < .5f && part.Type != PartType.Body)
                {
                    part.IsEnabled = false;
                }
                else
                {
                    part.IsEnabled = true;
                    part.SelectedVariant = part.Variants[Random.Range(0, part.Variants.Count)];
                }
            }

            await Task.Delay(1);

            SaveCombination();
        }

        private void SaveCombination()
        {
            var savedCombinations = new List<SavedPart>();
            foreach (var part in _parts)
            {
                var savedCombination = new SavedPart(part.Type, part.IsEnabled, part.VariantIndex);
                savedCombinations.Add(savedCombination);
            }
            _savedCombinations.Add(savedCombinations);

            while (_savedCombinations.Count > 4)
            {
                _savedCombinations.RemoveAt(0);
            }
        }

        private void Last()
        {
            var lastSavedCombination = _savedCombinations.Last();
            if (IsSame())
            {
                _savedCombinations.Remove(lastSavedCombination);
                lastSavedCombination = _savedCombinations.Last();
            }

            foreach (var part in _parts)
            {
                var savedCombination = lastSavedCombination.Find(c => c.PartType == part.Type);

                part.IsEnabled = savedCombination.IsEnabled;
                part.SelectVariant(savedCombination.VariantIndex);
            }

            _savedCombinations.Remove(lastSavedCombination);
        }

        private bool IsSame()
        {
            var lastSavedCombination = _savedCombinations.Last();
            foreach (var part in _parts)
            {
                var savedCombination = lastSavedCombination.Find(c => c.PartType == part.Type);

                if (part.IsEnabled != savedCombination.IsEnabled ||
                    part.VariantIndex != savedCombination.VariantIndex)
                {
                    return false;
                }
            }

            return true;
        }

        private void InitializeCamera()
        {
            if (_camera)
            {
                return;
            }

            _cameraPivot = new GameObject("CameraPivot").transform;
            _cameraPivot.gameObject.hideFlags = HideFlags.HideAndDontSave;

            var cameraObject = new GameObject("PreviewCamera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            _camera = cameraObject.AddComponent<Camera>();
            _camera.targetTexture = _renderTexture;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.renderingPath = RenderingPath.Forward;
            _camera.enabled = false;
            _camera.useOcclusionCulling = false;
            _camera.cameraType = CameraType.Preview;
            _camera.fieldOfView = 4.5f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.transform.SetParent(_cameraPivot);

            _cameraPivot.Rotate(Vector3.up, 150, Space.Self);
        }

        private void CreateRenderTexture()
        {
            if (_renderTexture)
            {
                return;
            }

            _renderTexture = new RenderTexture(300, 300, 30, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 8
            };
        }

        private void DrawMesh()
        {
            _camera.transform.localPosition = new Vector3(0, 1.1f, -36);

            foreach (var part in Parts.Where(part => part.IsEnabled))
            {
                DrawModel(part.SelectedVariant);
            }

            _camera.Render();
        }

        private void DrawModel(Variant variant)
        {
            for (var i = 0; i < variant.Materials.Length; i++)
            {
                Graphics.DrawMesh(variant.Mesh, new Vector3(0, -.01f, 0), Quaternion.identity, variant.Materials[i], 31, _camera, i);
            }
        }

        private IEnumerable<Part> LoadParts()
        {
            var assets = new List<Object>();
            var subFolders = AssetDatabase.GetSubFolders(AssetsPath.Parts);
            foreach (var subFolder in subFolders)
            {
                assets.AddRange(AssetDatabase.FindAssets("t:mesh", new[] { subFolder })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Select(AssetDatabase.LoadAllAssetsAtPath)
                    .SelectMany(assetsOfFbx => assetsOfFbx));
            }

            var renderers = new List<SkinnedMeshRenderer>();
            foreach (var asset in assets)
            {
                if (asset is SkinnedMeshRenderer r)
                {
                    renderers.Add(r);
                }
            }

            var parts = new List<Part>();
            var fbxs = AssetDatabase.LoadAllAssetsAtPath(AssetsPath.Fbx);
            foreach (var fbx in fbxs)
            {
                if (fbx is Mesh mesh)
                {
                    var partName = mesh.name.Split('_').First();

                    Part part;
                    if (partName == PartType.Full.ToString())
                    {
                        part = GetFullBodyPart();
                        part.IsEnabled = false;
                    }
                    else
                    {
                        var variants = renderers.Where(r => r.name.StartsWith(partName)).Select(r => new Variant(r.sharedMesh, CreateVariantPreview(r), r.sharedMaterials)).ToList();
                        part = new Part(Enum.Parse<PartType>(partName), variants);
                    }

                    parts.Add(part);
                }
            }

            var sortedParts = _partsOrder.Select(partType => parts.Find(p => p.Type == partType)).ToList();

            return sortedParts;
        }

        private Part GetFullBodyPart()
        {
            var assets = new List<Object>();

            assets.AddRange(AssetDatabase.FindAssets("t:mesh", new[] { AssetsPath.FullBody })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAllAssetsAtPath)
                .SelectMany(assetsOfFbx => assetsOfFbx));

            var renderers = new List<SkinnedMeshRenderer>();
            foreach (var asset in assets)
            {
                if (asset is SkinnedMeshRenderer r)
                {
                    renderers.Add(r);
                }
            }

            var variants = renderers.Select(r => new Variant(r.sharedMesh, CreateVariantPreview(r), r.sharedMaterials)).ToList();
            var part = new Part(PartType.Full, variants);

            return part;
        }

        private static GameObject CreateVariantPreview(SkinnedMeshRenderer r)
        {
            var variant = new GameObject(r.name);
            variant.AddComponent<MeshFilter>().sharedMesh = r.sharedMesh;
            var renderer = variant.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = r.sharedMaterials;
            variant.transform.position = Vector3.one * int.MaxValue;
            variant.hideFlags = HideFlags.HideAndDontSave;

            return variant;
        }

        private static void FindRoot()
        {
            var anchorAssetGuid = AssetDatabase.FindAssets(AssetsPath.BasicCharacterName).First();
            var anchorAssetPath = AssetDatabase.GUIDToAssetPath(anchorAssetGuid);
            var pathParts = anchorAssetPath.Split('/');
            var packTitleParts = AssetsPath.PackTitle.Split('_');
            var rootFound = false;
            for (var i = pathParts.Length - 1; i >= 0; i--)
            {
                if (rootFound)
                {
                    break;
                }

                foreach (var part in packTitleParts)
                {
                    rootFound = false;

                    if (!pathParts[i].Contains(part))
                    {
                        pathParts[i] = string.Empty;
                        break;
                    }

                    rootFound = true;
                }
            }

            var root = string.Join("/", pathParts.Where(p => !string.IsNullOrEmpty(p)).ToArray()) + "/";
            AssetsPath.SetRoot(root);
        }
    }
}