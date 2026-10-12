using BohShared;
using SecretHistories.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BookFinder;

/// <summary>
/// A softly pulsing thin gold frame drawn on the overlay canvas around a token for a few seconds.
/// The game's own AttentionPls is only a brief thin outline for small "Thing" items
/// (PhysicalManifestation) and easy to miss, so this marks the token regardless of its kind.
/// It follows the token while the camera flies there.
/// </summary>
public class TokenMarker : MonoBehaviour
{
    private const float Duration = 3f;
    private const float MinSize = 56f;
    private const float Padding = 6f;

    private Token _target;
    private RectTransform _rect;
    private RectTransform _parent;
    private CanvasGroup _group;
    private float _time;

    public static TokenMarker Show(Token target, RectTransform overlayParent, TokenMarker previous)
    {
        if (previous != null)
            Destroy(previous.gameObject);

        RectTransform rect = GameStyle.Rect("TokenMarker", overlayParent);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        var group = rect.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        // Thin frame only: a glow on top of the game's own highlight was too loud.
        Image frame = GameStyle.Panel(rect, "Frame", "information_goldborder", GameStyle.BrightGold, 2f);
        GameStyle.Stretch(frame.rectTransform);
        frame.raycastTarget = false;

        var marker = rect.gameObject.AddComponent<TokenMarker>();
        marker._target = target;
        marker._rect = rect;
        marker._parent = overlayParent;
        marker._group = group;
        marker.Follow();
        return marker;
    }

    private void Update()
    {
        _time += Time.unscaledDeltaTime;
        if (_target == null || !_target.gameObject.activeInHierarchy || _time > Duration)
        {
            Destroy(gameObject);
            return;
        }
        Follow();
        // Gentle pulse, then fade out over the last second.
        float pulse = 0.45f + 0.15f * Mathf.Sin(_time * Mathf.PI * 2f);
        _group.alpha = pulse * Mathf.Clamp01(Duration - _time);
    }

    private void Follow()
    {
        RectTransform tokenRect = _target.transform as RectTransform;
        if (tokenRect == null)
            return;

        // Token corners in screen pixels: world-space canvases go through the game camera,
        // screen-space overlay canvases (hand, trays) already are in pixels.
        var corners = new Vector3[4];
        tokenRect.GetWorldCorners(corners);
        Canvas canvas = tokenRect.GetComponentInParent<Canvas>();
        Camera camera = null;
        if (canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            camera = Watchman.Exists<CamOperator>() ? Watchman.Get<CamOperator>().GetAttachedCamera() : Camera.main;

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 corner in corners)
        {
            Vector2 screen = camera != null ? (Vector2)camera.WorldToScreenPoint(corner) : (Vector2)corner;
            min = Vector2.Min(min, screen);
            max = Vector2.Max(max, screen);
        }

        // Screen pixels -> overlay canvas units.
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent, (min + max) / 2f, null, out Vector2 center);
        float scaleFactor = _parent.GetComponentInParent<Canvas>().rootCanvas.scaleFactor;
        Vector2 size = (max - min) / scaleFactor + new Vector2(Padding * 2f, Padding * 2f);
        _rect.localPosition = new Vector3(center.x, center.y, 0f);
        _rect.sizeDelta = Vector2.Max(size, new Vector2(MinSize, MinSize));
    }
}
