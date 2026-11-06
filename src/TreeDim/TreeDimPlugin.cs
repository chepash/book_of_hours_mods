using System.Reflection;
using BepInEx;
using HarmonyLib;
using SecretHistories.Core;
using SecretHistories.Entities;
using SecretHistories.Spheres;
using SecretHistories.UI;
using UnityEngine;
using UnityEngine.UI;

namespace TreeDim;

/// <summary>
/// While the Tree of Wisdoms is open, skills already committed to a Wisdom (aspect
/// wisdom.committed) are darkened in the skills tray, so the uncommitted ones stand out.
/// The tree is the Wisdoms sphere: Unshroud opens it, Shroud closes it. Darkening is an overlay
/// image on each committed skill token rather than the token's alpha, which the game drives itself.
/// While open, the tray is rechecked twice a second: committing a skill there must darken it.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class TreeDimPlugin : BaseUnityPlugin
{
    public const string Guid = "shirofaii.boh.treedim";
    public const string Name = "Tree Dim";
    public const string Version = "0.1.0";

    private const string OverlayName = "TreeDimOverlay";
    private const string SkillsTray = "~/hand.skills";
    private static readonly Color Shade = new Color(0f, 0f, 0f, 0.62f);
    private static readonly FieldInfo ShroudedField = AccessTools.Field(typeof(Wisdoms), "_shrouded");

    private static bool _open;
    private Harmony _harmony;
    private float _nextRefresh;

    private void Awake()
    {
        _harmony = new Harmony(Guid);
        _harmony.PatchAll(typeof(WisdomsPatch));
        // Reloaded while the tree is open: pick up its state once.
        Wisdoms wisdoms = FindObjectOfType<Wisdoms>();
        _open = wisdoms != null && ShroudedField != null && !(bool)ShroudedField.GetValue(wisdoms);
        Logger.LogInfo($"{Name} {Version} loaded");
    }

    private void OnDestroy()
    {
        // Hot reload: remove overlays at once (their fade component goes with the old assembly).
        _harmony?.UnpatchSelf();
        _open = false;
        foreach (DimFade fade in Resources.FindObjectsOfTypeAll<DimFade>())
        {
            if (fade != null)
                Destroy(fade.gameObject);
        }
    }

    private void Update()
    {
        if (!_open || Time.unscaledTime < _nextRefresh)
            return;
        _nextRefresh = Time.unscaledTime + 0.5f;
        Apply();
    }

    [HarmonyPatch(typeof(Wisdoms))]
    private static class WisdomsPatch
    {
        [HarmonyPostfix, HarmonyPatch(nameof(Wisdoms.Unshroud))]
        private static void Opened()
        {
            _open = true;
            Apply();
        }

        [HarmonyPostfix, HarmonyPatch(nameof(Wisdoms.Shroud))]
        private static void Closed()
        {
            _open = false;
            Apply();
        }
    }

    /// <summary>Overlay on committed skills while the tree is open; none otherwise.</summary>
    private static void Apply()
    {
        if (!Watchman.Exists<HornedAxe>())
            return;
        Sphere tray = Watchman.Get<HornedAxe>().GetSphereByAbsolutePath(SkillsTray);
        if (tray == null || !tray.IsValid())
            return;
        foreach (Token token in tray.Tokens)
        {
            if (token == null || !(token.Payload is ElementStack stack))
                continue;
            bool dim = _open && stack.GetAspects(true).AspectValue("wisdom.committed") > 0;
            Transform overlay = token.transform.Find(OverlayName);
            if (dim && overlay == null)
                overlay = AddOverlay(token.transform);
            // Fades in and out (DimFade), and removes itself once faded out.
            DimFade fade = overlay != null ? overlay.GetComponent<DimFade>() : null;
            if (fade != null)
                fade.Target = dim ? Shade.a : 0f;
            else if (overlay != null)
                Destroy(overlay.gameObject); // left by an older version of this plugin
        }
    }

    private static Transform AddOverlay(Transform token)
    {
        var go = new GameObject(OverlayName, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(token, false);
        rect.SetAsLastSibling();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        go.AddComponent<LayoutElement>().ignoreLayout = true;
        Image image = go.AddComponent<Image>();
        image.sprite = BohShared.GameStyle.Sprite("box-rnd-16px");
        image.type = Image.Type.Sliced;
        image.color = new Color(Shade.r, Shade.g, Shade.b, 0f);
        image.raycastTarget = false; // the card stays draggable and clickable
        go.AddComponent<DimFade>().Image = image;
        return rect;
    }
}

/// <summary>Moves the overlay's alpha towards Target over about a third of a second; gone at zero.</summary>
public class DimFade : MonoBehaviour
{
    private const float Duration = 0.35f;
    public Image Image;
    public float Target;

    private void Update()
    {
        if (Image == null)
            return;
        Color c = Image.color;
        float step = Time.unscaledDeltaTime / Duration * 0.62f;
        c.a = Mathf.MoveTowards(c.a, Target, step);
        Image.color = c;
        if (Target <= 0f && c.a <= 0f)
            Destroy(gameObject);
    }
}
