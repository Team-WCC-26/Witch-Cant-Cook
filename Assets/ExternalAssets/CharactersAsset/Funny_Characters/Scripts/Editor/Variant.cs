using UnityEngine;

namespace CharacterCustomization
{
    public class Variant
    {
        public readonly GameObject PreviewObject;
        public readonly Mesh Mesh;
        public Material[] Materials;

        public string Name => Mesh.name;

        public Variant(Mesh mesh, GameObject previewObject, Material[] materials)
        {
            Mesh = mesh;
            PreviewObject = previewObject;
            Materials = materials;
        }
    }
}