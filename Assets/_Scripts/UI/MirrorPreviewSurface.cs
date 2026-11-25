using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public sealed class MirrorPreviewSurface : MonoBehaviour
{
    [SerializeField] private GameObject previewCharacter;
    [SerializeField] private Sprite blockedIcon;

    private static MirrorPreviewRig sceneRig;
    private MirrorPreviewRig rig;
    private RawImage surface;

    internal Sprite BlockedIcon => blockedIcon;

    #region Unity Callbacks
    private void OnEnable()
    {
        surface = GetComponent<RawImage>();
        surface.enabled = false;
        if (previewCharacter == null) return;

        Scene scene = gameObject.scene;
        if (sceneRig == null || sceneRig.gameObject.scene != scene)
        {
            var rigObj = new GameObject("Costume Mirror Rig");
            SceneManager.MoveGameObjectToScene(rigObj, scene);
            GameObject previewRoot = GameObject.Find("MirrorPreview");
            if (previewRoot != null && previewRoot.scene == scene)
                rigObj.transform.SetParent(previewRoot.transform, false);
            rigObj.transform.localPosition = Vector3.up * 10000f;
            sceneRig = rigObj.AddComponent<MirrorPreviewRig>();
            sceneRig.Initialize(previewCharacter, gameObject.layer);
        }

        rig = sceneRig;
        rig.gameObject.SetActive(true);
        rig.Reframe();
        surface.texture = rig.Texture;
        surface.enabled = true;
    }

    private void OnDisable()
    {
        if (surface != null)
        {
            surface.texture = null;
            surface.enabled = false;
        }
        if (rig != null) rig.gameObject.SetActive(false);
    }
    #endregion

    internal void ApplySlots(string[] names, ushort[] slots)
    {
        if (rig != null) rig.ApplySlots(names, slots);
    }
}
