using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BohShared;

/// <summary>
/// The game's own sprites, fonts and colours, looked up by name.
/// Names come from DevTools UI dumps of the room (TerrainDetailWindow) and details windows.
/// </summary>
public static class GameStyle
{
    public static readonly Color Cream = new Color32(0xF8, 0xEE, 0xD9, 0xFF);
    public static readonly Color Gold = new Color32(0xA3, 0x88, 0x51, 0xFF);
    public static readonly Color GoldFaint = new Color32(0xA3, 0x88, 0x51, 0x94);
    /// <summary>Glow colour of slot highlights (TerrainDetailWindow "Glow").</summary>
    public static readonly Color BrightGold = new Color32(0xFD, 0xE3, 0x97, 0xFF);

    private static Dictionary<string, Sprite> _sprites;
    private static TMP_FontAsset _font;

    public static TMP_FontAsset Font
    {
        get
        {
            if (_font == null)
            {
                foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (font.name == "EBGaramond08-Regular SDF")
                    {
                        _font = font;
                        break;
                    }
                }
            }
            return _font;
        }
    }

    public static Sprite Sprite(string name)
    {
        if (_sprites == null)
        {
            _sprites = new Dictionary<string, Sprite>();
            foreach (Sprite sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sprite != null && !_sprites.ContainsKey(sprite.name))
                    _sprites[sprite.name] = sprite;
            }
        }
        return _sprites.TryGetValue(name, out Sprite found) && found != null ? found : null;
    }

    public static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    public static void Stretch(RectTransform rect, float left = 0, float top = 0, float right = 0, float bottom = 0)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    public static Image Panel(Transform parent, string name, string sprite, Color color, float pixelsPerUnit = 1f)
    {
        RectTransform rect = Rect(name, parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.sprite = Sprite(sprite);
        image.type = image.sprite != null && image.sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        image.pixelsPerUnitMultiplier = pixelsPerUnit;
        image.color = color;
        return image;
    }

    /// <summary>Full-size background that the parent's layout group ignores.</summary>
    public static Image Background(Transform parent, string sprite, Color color, float pixelsPerUnit = 1f)
    {
        Image image = Panel(parent, "BG_" + sprite, sprite, color, pixelsPerUnit);
        Stretch(image.rectTransform);
        image.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        image.transform.SetAsFirstSibling();
        return image;
    }

    public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color,
        FontStyles style = FontStyles.Normal, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
    {
        RectTransform rect = Rect(name, parent);
        var tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.font = Font;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.raycastTarget = false;
        tmp.text = text;
        return tmp;
    }

    public static LayoutElement Size(Component c, float width = -1, float height = -1, float flexibleWidth = -1)
    {
        LayoutElement le = c.GetComponent<LayoutElement>() ?? c.gameObject.AddComponent<LayoutElement>();
        if (width >= 0) le.minWidth = le.preferredWidth = width;
        if (height >= 0) le.minHeight = le.preferredHeight = height;
        if (flexibleWidth >= 0) le.flexibleWidth = flexibleWidth;
        return le;
    }

    public static T Layout<T>(Component c, float spacing, RectOffset padding = null, TextAnchor align = TextAnchor.MiddleLeft)
        where T : HorizontalOrVerticalLayoutGroup
    {
        var group = c.gameObject.AddComponent<T>();
        group.spacing = spacing;
        group.padding = padding ?? new RectOffset();
        group.childAlignment = align;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = false;
        return group;
    }

    public static TMP_InputField InputField(Transform parent, string placeholder)
    {
        RectTransform rect = Rect("SearchInput", parent);
        rect.gameObject.SetActive(false); // configure before TMP_InputField.OnEnable runs
        var bg = rect.gameObject.AddComponent<Image>();
        bg.sprite = Sprite("editableillum_bg");
        bg.type = Image.Type.Sliced;
        bg.pixelsPerUnitMultiplier = 2f;

        RectTransform area = Rect("Text Area", rect);
        Stretch(area, 12, 4, 12, 4);
        area.gameObject.AddComponent<RectMask2D>();

        TextMeshProUGUI hint = Text(area, "Placeholder", placeholder, 18, GoldFaint, FontStyles.Italic);
        Stretch(hint.rectTransform);
        TextMeshProUGUI text = Text(area, "Text", "", 20, Gold);
        Stretch(text.rectTransform);

        var input = rect.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = area;
        input.textComponent = text;
        input.placeholder = hint;
        input.fontAsset = Font;
        input.pointSize = 20;
        input.caretColor = Cream;
        input.selectionColor = new Color(Gold.r, Gold.g, Gold.b, 0.4f);
        input.lineType = TMP_InputField.LineType.SingleLine;
        input.targetGraphic = bg;
        rect.gameObject.SetActive(true);
        return input;
    }
}
