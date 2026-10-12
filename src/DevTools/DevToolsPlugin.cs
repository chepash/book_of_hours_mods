using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using SecretHistories.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace DevTools;

/// <summary>
/// Development helpers, not meant for players.
/// Ctrl+Shift+D dumps the UI hierarchy of key game windows (sprites, fonts, colours, layout)
/// into BepInEx\dumps, so mods can copy the game's look without guessing.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class DevToolsPlugin : BaseUnityPlugin
{
    public const string Guid = "shirofaii.boh.devtools";
    public const string Name = "Dev Tools";
    public const string Version = "0.1.0";

    private static readonly FieldInfo MasterDetailWindowField = AccessTools.Field(typeof(AureateNotifier), "masterDetailWindow");

    private string DumpDir => Path.Combine(Paths.BepInExRootPath, "dumps");

    private void Awake()
    {
        Logger.LogInfo($"{Name} {Version} loaded");
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.ctrlKey.isPressed || !keyboard.shiftKey.isPressed || !keyboard.dKey.wasPressedThisFrame)
            return;
        try
        {
            DumpAll();
        }
        catch (Exception e)
        {
            Logger.LogError(e);
        }
    }

    private void DumpAll()
    {
        Directory.CreateDirectory(DumpDir);

        if (Watchman.Exists<AureateNotifier>() && MasterDetailWindowField.GetValue(Watchman.Get<AureateNotifier>()) is Component master && master != null)
            WriteDump("MasterDetailWindow", master.transform, int.MaxValue);

        TerrainDetailWindow terrain = Resources.FindObjectsOfTypeAll<TerrainDetailWindow>().FirstOrDefault(w => w.gameObject.scene.IsValid());
        if (terrain != null)
            WriteDump("TerrainDetailWindow", terrain.transform, int.MaxValue);

        foreach (Canvas canvas in Resources.FindObjectsOfTypeAll<Canvas>().Where(c => c.isRootCanvas && c.gameObject.scene.IsValid()))
            WriteDump("Canvas_" + Sanitize(canvas.name), canvas.transform, 4);

        var fonts = new StringBuilder();
        foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>().OrderBy(f => f.name))
            fonts.AppendLine(font.name);
        File.WriteAllText(Path.Combine(DumpDir, "fonts.txt"), fonts.ToString());

        Logger.LogMessage("UI dumped to " + DumpDir);
    }

    private void WriteDump(string name, Transform root, int maxDepth)
    {
        var sb = new StringBuilder();
        Canvas canvas = root.GetComponentInParent<Canvas>();
        if (canvas != null)
            sb.AppendLine($"# canvas={canvas.rootCanvas.name} mode={canvas.rootCanvas.renderMode} scale={canvas.rootCanvas.scaleFactor}");
        DumpNode(sb, root, 0, maxDepth);
        File.WriteAllText(Path.Combine(DumpDir, name + ".txt"), sb.ToString());
    }

    private static void DumpNode(StringBuilder sb, Transform t, int depth, int maxDepth)
    {
        string indent = new string(' ', depth * 2);
        string rect = t is RectTransform rt
            ? $" size={rt.rect.width:0}x{rt.rect.height:0} anchors=({rt.anchorMin.x:0.##},{rt.anchorMin.y:0.##})-({rt.anchorMax.x:0.##},{rt.anchorMax.y:0.##}) pivot=({rt.pivot.x:0.##},{rt.pivot.y:0.##})"
            : "";
        sb.AppendLine($"{indent}{t.name}{(t.gameObject.activeSelf ? "" : " [inactive]")}{rect}");

        foreach (Component c in t.GetComponents<Component>())
        {
            if (c == null || c is Transform)
                continue;
            sb.AppendLine($"{indent}  - {Describe(c)}");
        }

        if (depth >= maxDepth)
        {
            if (t.childCount > 0)
                sb.AppendLine($"{indent}  ... {t.childCount} children");
            return;
        }
        for (int i = 0; i < t.childCount; i++)
            DumpNode(sb, t.GetChild(i), depth + 1, maxDepth);
    }

    private static string Describe(Component c)
    {
        switch (c)
        {
            case TMP_InputField input:
                return $"TMP_InputField text='{input.text}' textComponent={input.textComponent?.name} placeholder={input.placeholder?.name}";
            case TextMeshProUGUI tmp:
                return $"TMP font={tmp.font?.name} mat={tmp.fontSharedMaterial?.name} size={tmp.fontSize} auto={tmp.enableAutoSizing} style={tmp.fontStyle} color={Hex(tmp.color)} align={tmp.alignment} text='{Short(tmp.text)}'";
            case Image img:
                return $"Image sprite={img.sprite?.name} tex={img.sprite?.texture?.name} type={img.type} color={Hex(img.color)} border={img.sprite?.border} ppu={img.pixelsPerUnitMultiplier} raycast={img.raycastTarget} mat={img.material?.name}";
            case RawImage raw:
                return $"RawImage tex={raw.texture?.name} color={Hex(raw.color)}";
            case HorizontalOrVerticalLayoutGroup lg:
                return $"{lg.GetType().Name} pad=({lg.padding.left},{lg.padding.right},{lg.padding.top},{lg.padding.bottom}) spacing={lg.spacing} align={lg.childAlignment} ctrlW={lg.childControlWidth} ctrlH={lg.childControlHeight} expW={lg.childForceExpandWidth} expH={lg.childForceExpandHeight}";
            case GridLayoutGroup grid:
                return $"GridLayoutGroup cell={grid.cellSize} spacing={grid.spacing}";
            case LayoutElement le:
                return $"LayoutElement ignore={le.ignoreLayout} min=({le.minWidth},{le.minHeight}) pref=({le.preferredWidth},{le.preferredHeight}) flex=({le.flexibleWidth},{le.flexibleHeight})";
            case ContentSizeFitter fit:
                return $"ContentSizeFitter h={fit.horizontalFit} v={fit.verticalFit}";
            case CanvasGroup group:
                return $"CanvasGroup alpha={group.alpha} raycasts={group.blocksRaycasts}";
            case Button button:
                return $"Button transition={button.transition} target={button.targetGraphic?.name}";
            case ScrollRect scroll:
                return $"ScrollRect content={scroll.content?.name} viewport={scroll.viewport?.name} vbar={scroll.verticalScrollbar?.name} sens={scroll.scrollSensitivity}";
            case Scrollbar bar:
                return $"Scrollbar dir={bar.direction} handle={bar.handleRect?.name}";
            case Mask mask:
                return $"Mask showGraphic={mask.showMaskGraphic}";
            default:
                return c.GetType().FullName;
        }
    }

    private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);

    private static string Short(string s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        s = s.Replace("\n", "\\n");
        return s.Length > 40 ? s.Substring(0, 40) + "…" : s;
    }

    private static string Sanitize(string s) => string.Concat(s.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
}
