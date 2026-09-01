using UnityEngine;

public class PathMaterialPropertyOverrides : MonoBehaviour
{
    public float TilingY = 1f;
    public float TilingX = 1f;
    public float Length = 1f;

    private void OnValidate()
    {
        SetData();
    }

    private void OnEnable()
    {
        SetData();
    }

    private void SetData()
    {
        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            Debug.LogError($"Mesh Renderer on Path: {gameObject.name} is NOT Valid");
            return;
        }

        MaterialPropertyBlock mpc = new MaterialPropertyBlock();

        mpc.SetFloat("_Tiling", TilingY);
        mpc.SetFloat("_Tiling_X", TilingX);
        mpc.SetFloat("_Length", Length / 100f);

        meshRenderer.SetPropertyBlock(mpc);
    }
}
