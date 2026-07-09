using UnityEngine;
using UnityEngine.UI;

// Shared shader-based rounded-corner clip for card images — the same anti-aliased
// "transparent mask on the edges" treatment GameManager uses in-game, exposed as a
// one-call utility so EVERY surface that shows a card (deck builder tiles, leader
// slots, previews, lobby thumbs, showcases) gets the identical uniform rounding.
// Uses the UI/RoundedCard shader; each image gets its own material instance so
// radii stay independent per card. RectMask2D-safe.
public static class RoundedCardMask
{
    // Matches GameManager.RoundedCornerFraction so in-game and menu cards look identical.
    public const float CornerFraction = 0.04f;

    private static Material _baseMaterial;

    private static Material BaseMaterial
    {
        get
        {
            if (_baseMaterial == null) _baseMaterial = new Material(Shader.Find("UI/RoundedCard"));
            return _baseMaterial;
        }
    }

    /// <summary>Clips this Image to a rounded card rect (anti-aliased shader mask).</summary>
    public static void ApplyTo(Image image, float fraction = CornerFraction)
    {
        if (image == null) return;
        image.material = BaseMaterial;
        var clip = image.gameObject.GetComponent<Driver>();
        if (clip == null) clip = image.gameObject.AddComponent<Driver>();
        clip.Init(image, fraction);
    }

    // Feeds the shader the image's pixel size + radius each frame (identical to
    // GameManager.RoundedCardClip).
    public sealed class Driver : MonoBehaviour
    {
        private static readonly int SizeID = Shader.PropertyToID("_Size");
        private static readonly int RadiusID = Shader.PropertyToID("_Radius");
        private Material mat;
        private RectTransform rt;
        private float fraction;

        public void Init(Graphic graphic, float radiusFraction)
        {
            rt = graphic.rectTransform;
            if (mat != null) Destroy(mat);
            mat = Object.Instantiate(graphic.material);
            graphic.material = mat;
            fraction = radiusFraction;
        }

        private void Update()
        {
            if (mat == null || rt == null) return;
            Vector2 s = rt.rect.size;
            if (s.x <= 1f || s.y <= 1f) return;
            mat.SetVector(SizeID, new Vector4(s.x, s.y, 0f, 0f));
            mat.SetFloat(RadiusID, Mathf.Min(s.x, s.y) * fraction);
        }

        private void OnDestroy()
        {
            if (mat != null) Destroy(mat);
        }
    }
}
