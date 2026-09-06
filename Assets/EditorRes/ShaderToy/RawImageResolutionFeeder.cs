using UnityEngine;
using UnityEngine.UI;

// Keeps a raymarch shader's _Resolution in sync with the RawImage's own rect so the
// shader's internal camera/aspect math is based on the element's shape, not the screen's.
[RequireComponent(typeof(RawImage))]
[ExecuteAlways]
public sealed class RawImageResolutionFeeder : MonoBehaviour
{
    private RawImage rawImage;
    private RectTransform rectTransform;
    private Material materialInstance;

    private void OnEnable()
    {
        rawImage = GetComponent<RawImage>();
        rectTransform = (RectTransform)transform;
        materialInstance = rawImage.material = new Material(rawImage.material);
    }

    private void OnDisable()
    {
        if (materialInstance != null) DestroyImmediate(materialInstance);
    }

    private void Update()
    {
        Rect rect = rectTransform.rect;
        materialInstance.SetVector("_Resolution", new Vector4(Mathf.Max(rect.width, 1f), Mathf.Max(rect.height, 1f), 0f, 0f));
    }
}
