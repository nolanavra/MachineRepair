using UnityEngine;

/// <summary>
/// Forces a SpriteRenderer to match a specific world-space size,
/// regardless of sprite PPU, resolution, or import settings.
/// </summary>
[ExecuteAlways]
public class ForceRealWorldSize : MonoBehaviour
{
    [Tooltip("Target size in world units (e.g., 50.8cm x 38.8cm).")]
    public Vector2 targetSize = new Vector2(50.8f, 38.8f);

    private SpriteRenderer sr;

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
    }

    void Start()
    {
        ApplySize();
    }

#if UNITY_EDITOR
    void Update()  // live-updates while editing
    {
        if (!Application.isPlaying)
            ApplySize();
    }
#endif

    private void ApplySize()
    {
        if (sr == null || sr.sprite == null)
            return;

        // Sprite's current size in world units (before scaling)
        Vector2 spriteSize = sr.sprite.bounds.size;

        // Compute the scale needed to reach the target size
        Vector3 newScale = new Vector3(
            targetSize.x / spriteSize.x,
            targetSize.y / spriteSize.y,
            1f
        );

        transform.localScale = newScale;
    }
}
