using UnityEngine;

public enum PotType
{
    Soup,
    Stew
}

[CreateAssetMenu(menuName = "Scriptable Objects/Cooking Visual/Pot")]
public class PotVisualData : ScriptableObject
{
    [SerializeField] private PotType potType;
    [SerializeField] private int ingredientId;
    [SerializeField] private Mesh potMesh;
    [SerializeField] private Material[] potMaterials;
    [SerializeField] private Vector3 scale = new Vector3(0.3f, 0.3f, 0.3f);

    public PotType PotType => potType;
    public int IngredientId => ingredientId;
    public Mesh PotMesh => potMesh;
    public Material[] PotMaterials => potMaterials;
    public bool HasPotVisual => potMesh != null;
    public Vector3 Scale => scale;
}