using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BohShared;
using HarmonyLib;
using SecretHistories.Core;
using SecretHistories.Entities;
using SecretHistories.Enums;
using SecretHistories.Infrastructure;
using SecretHistories.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CopyBreaks;

/// <summary>
/// "Book text" in a desk's window once a book has been read: copies the book's title and
/// description, its "I'm Reading..." and its "I've Read..." texts in one go, in the order the
/// player used to copy them one by one. The texts come from the book's element: Label, Desc and
/// Xexts "reading.&lt;mystery&gt;.intro" / "reading.&lt;mystery&gt;", for the mystery it has
/// mastered (mastery.&lt;mystery&gt;). Shown just left of Collect while the window shows a
/// finished recipe, so both buttons are in one place; copied through the game's own
/// ClipboardTextCopier, so the "copied" notification and CopyBreaks' line breaks apply.
/// A first reading (it gave a lesson: a card with the "lesson" aspect among the results, recipe
/// study.mystery.&lt;p&gt;.mastering.yes) gets a red border - the texts worth copying for the notes;
/// a reread (".mastered", no lesson) keeps the gold one.
/// </summary>
internal static class ReadingCopyButton
{
    private const string ButtonName = "CopyReadingButton";

    [HarmonyPatch(typeof(AureateWindow), "DisplayButtonState")]
    internal static class WindowPatch
    {
        internal static void Postfix(AureateWindow __instance, Situation situation) => Refresh(__instance, situation);
    }

    internal static void Refresh(AureateWindow window, Situation situation)
    {
        if (!(StartField.GetValue(window) is Button start) || start == null)
            return;
        Transform bar = start.transform.parent;
        Transform existing = bar.Find(ButtonName);
        string text = situation != null && situation.TimeRemaining <= 0f && situation.StateIdentifier != StateEnum.Unstarted
            ? ReadBookText(situation)
            : null;
        if (text == null)
        {
            if (existing != null)
                existing.gameObject.SetActive(false);
            return;
        }
        GameObject button = existing != null ? existing.gameObject : Create(bar, start);
        button.SetActive(true);
        button.GetComponent<CopyTarget>().Text = text;
        bool first = IsFirstReading(situation);
        button.GetComponent<CopyButtonFx>().SetOutline(first ? CopyButtonFx.Red : GameStyle.Gold,
            first ? CopyButtonFx.RedLit : GameStyle.BrightGold);
    }

    private static bool IsFirstReading(Situation situation)
    {
        string recipe = situation.GetCurrentRecipe()?.Id ?? "";
        if (recipe.Contains(".mastering.yes"))
            return true;
        return situation.GetElementTokensInSituation()
            .Any(t => t.Payload is ElementStack s && s.GetAspects(true).AspectValue("lesson") > 0);
    }

    /// <summary>The finished book's texts, or null if no mastered readable is in the window.</summary>
    private static string ReadBookText(Situation situation)
    {
        foreach (Token token in situation.GetElementTokensInSituation())
        {
            if (!(token.Payload is ElementStack stack) || stack.Element?.Xexts == null)
                continue;
            AspectsDictionary aspects = stack.GetAspects(true);
            if (aspects.AspectValue("readable") <= 0)
                continue;
            string mystery = aspects.Keys.Where(k => k.StartsWith("mastery.") && aspects.AspectValue(k) > 0)
                .Select(k => k.Substring("mastery.".Length)).FirstOrDefault();
            if (mystery == null)
                continue;
            Element book = stack.Element;
            book.Xexts.TryGetValue($"reading.{mystery}.intro", out string reading);
            book.Xexts.TryGetValue($"reading.{mystery}", out string read);
            var sb = new StringBuilder();
            sb.Append(Clean(book.Label)).Append("\n\n").Append(Clean(book.Desc));
            if (!string.IsNullOrEmpty(reading))
                sb.Append("\n\nI'm Reading...\n\n").Append(Clean(reading));
            if (!string.IsNullOrEmpty(read))
                sb.Append("\n\nI've Read...\n\n").Append(Clean(read));
            return sb.ToString();
        }
        return null;
    }

    /// <summary>Rich-text tags (&lt;i&gt;, &lt;sprite name=heart&gt;) off, as the game's own copy does.</summary>
    private static string Clean(string s) =>
        string.IsNullOrEmpty(s) ? "" : Regex.Replace(s, "<sprite name=([a-z.]+)>", "$1").Replace("<i>", "").Replace("</i>", "").Trim();

    /// <summary>
    /// Left of Collect (the window's start button), same height and the same rounded "boh button"
    /// plate: the border is that sprite tinted (red or gold) with a darker copy laid inside it.
    /// </summary>
    private static GameObject Create(Transform bar, Button start)
    {
        Image outline = GameStyle.Panel(bar, ButtonName, ButtonSprite, GameStyle.Gold);
        RectTransform rect = outline.rectTransform;
        var startRect = (RectTransform)start.transform;
        rect.anchorMin = rect.anchorMax = startRect.anchorMin;
        rect.pivot = new Vector2(1f, startRect.pivot.y);
        rect.sizeDelta = new Vector2(120f, startRect.rect.height);
        rect.anchoredPosition = startRect.anchoredPosition + new Vector2(-startRect.rect.width * startRect.pivot.x - 12f, 0f);
        outline.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        Image plate = GameStyle.Panel(rect, "Plate", ButtonSprite, PlateNormal);
        GameStyle.Stretch(plate.rectTransform, 2.5f, 2.5f, 2.5f, 2.5f);
        plate.raycastTarget = false;
        TextMeshProUGUI label = GameStyle.Text(rect, "Label", "Book text", 16f, GameStyle.Cream, FontStyles.SmallCaps,
            TextAlignmentOptions.Center);
        GameStyle.Stretch(label.rectTransform);
        label.raycastTarget = false;

        var target = outline.gameObject.AddComponent<CopyTarget>();
        // Hover, press and "Copied!" feedback drawn by hand: a ColorTint transition multiplies the
        // white background and is capped at 1, so it showed nothing.
        var fx = outline.gameObject.AddComponent<CopyButtonFx>();
        fx.SetPlate(PlateNormal, PlateHover, PlatePressed);
        fx.Init(plate, outline, label);
        Button button = outline.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() =>
        {
            ClipboardTextCopier.CopyTextToClipboard(target.Text);
            fx.Flash();
        });
        return outline.gameObject;
    }

    private const string ButtonSprite = "boh button"; // Collect's own plate
    // The plate sprite is light gold, so these tints darken it to the panel's tone.
    private static readonly Color PlateNormal = new Color(0.13f, 0.11f, 0.10f, 1f);
    private static readonly Color PlateHover = new Color(0.24f, 0.20f, 0.16f, 1f);
    private static readonly Color PlatePressed = new Color(0.08f, 0.07f, 0.06f, 1f);

    private static readonly System.Reflection.FieldInfo PayloadField = AccessTools.Field(typeof(AureateWindow), "_payload");
    private static readonly System.Reflection.FieldInfo StartField = AccessTools.Field(typeof(AureateWindow), "startButton");

    /// <summary>Windows already open when the plugin (re)loads: a finished recipe won't call DisplayButtonState again.</summary>
    internal static System.Collections.IEnumerator FillOpenWindows()
    {
        yield return null; // let the previous instance clean up first
        foreach (AureateWindow window in Resources.FindObjectsOfTypeAll<AureateWindow>())
        {
            if (window != null && window.gameObject.scene.IsValid() && PayloadField.GetValue(window) is Situation situation)
                Refresh(window, situation);
        }
    }

    internal static void RemoveAll()
    {
        foreach (CopyTarget target in Resources.FindObjectsOfTypeAll<CopyTarget>())
        {
            if (target != null)
                Object.Destroy(target.gameObject);
        }
    }
}

/// <summary>Hover lights the border and label, a press shrinks the button a little, a click shows "Copied!".</summary>
public class CopyButtonFx : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler,
    UnityEngine.EventSystems.IPointerExitHandler, UnityEngine.EventSystems.IPointerDownHandler,
    UnityEngine.EventSystems.IPointerUpHandler
{
    internal static readonly Color BgNormal = new Color(1f, 1f, 1f, 0.55f);
    // Pressed: darker plate, like the game's own ColorTint buttons; size never changes.
    private Color _bgNormal = BgNormal, _bgHover = Color.white, _bgPressed = new Color(0.6f, 0.6f, 0.6f, 1f);
    private string _caption; // the label set when created ("Book text", "Conversations")
    /// <summary>Border colours, normal and lit; gold unless a button wants to stand out.</summary>
    internal Color OutlineNormal = GameStyle.Gold;
    internal Color OutlineLit = GameStyle.BrightGold;
    internal static readonly Color Red = new Color(0.9f, 0.18f, 0.15f, 1f);
    internal static readonly Color RedLit = new Color(1f, 0.45f, 0.4f, 1f);

    /// <summary>Background tints for a button whose plate isn't the white "slots_bg".</summary>
    internal void SetPlate(Color normal, Color hover, Color pressed)
    {
        _bgNormal = normal;
        _bgHover = hover;
        _bgPressed = pressed;
        Apply();
    }

    internal void SetOutline(Color normal, Color lit)
    {
        OutlineNormal = normal;
        OutlineLit = lit;
        Apply();
    }

    private Image _bg;
    private Image _outline;
    private TextMeshProUGUI _label;
    private bool _hover;
    private bool _pressed;
    private float _flashUntil;

    internal void Init(Image bg, Image outline, TextMeshProUGUI label)
    {
        _bg = bg;
        _outline = outline;
        _label = label;
        _caption = label.text;
        Apply();
    }

    internal void Flash()
    {
        _flashUntil = Time.unscaledTime + 1.2f;
        Apply();
    }

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) { _hover = true; Apply(); }

    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e)
    {
        _hover = false;
        _pressed = false;
        Apply();
    }

    public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e) { _pressed = true; Apply(); }

    public void OnPointerUp(UnityEngine.EventSystems.PointerEventData e) { _pressed = false; Apply(); }

    private void Update()
    {
        if (_flashUntil > 0f && Time.unscaledTime > _flashUntil)
        {
            _flashUntil = 0f;
            Apply();
        }
    }

    private void OnDisable()
    {
        _hover = false;
        _flashUntil = 0f;
        _pressed = false;
        Apply();
    }

    private void Apply()
    {
        if (_bg == null)
            return;
        bool flashing = _flashUntil > 0f;
        bool lit = _hover || flashing;
        _bg.color = _pressed ? _bgPressed : lit ? _bgHover : _bgNormal;
        _outline.color = lit ? OutlineLit : OutlineNormal;
        _label.color = lit ? GameStyle.BrightGold : GameStyle.Cream;
        _label.text = flashing ? "Copied!" : _caption;
    }
}

/// <summary>Holds the text the "Book text" button copies; also marks the button for clean-up.</summary>
public class CopyTarget : MonoBehaviour
{
    public string Text;
}
