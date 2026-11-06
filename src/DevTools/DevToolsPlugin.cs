using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using SecretHistories.Infrastructure;
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

    /// <summary>
    /// A save requested from outside the game: tools (Claude, recomputing the vault's tree plan)
    /// create this file and the game saves within a few seconds, then the file is removed.
    /// </summary>
    private static string SaveRequestPath => Path.Combine(Paths.CachePath, "save-request");

    /// <summary>
    /// A card requested from outside the game (one element id per line), put into the hand's
    /// memories row (hand.memories) or, failing that, the first hand sphere. For giving back a
    /// card the player lost by mistake.
    /// </summary>
    private static string SpawnRequestPath => Path.Combine(Paths.CachePath, "spawn-request");

    /// <summary>
    /// Cards to take away (one element id per line, one card each), from anywhere outside windows
    /// and decks: undoing rewards the player got by a choice they regret.
    /// </summary>
    private static string RemoveRequestPath => Path.Combine(Paths.CachePath, "remove-request");

    /// <summary>
    /// Constellation (Nx node) story cards to swap, "from to" per line: the node's story card lives in
    /// its private internal sphere; replacing it puts the story back on an earlier step. The card is
    /// retired and a new one provisioned (not ElementStack.ChangeTo), so the node hears the sphere
    /// change and refreshes its title, text and choices.
    /// </summary>
    private static string NxRequestPath => Path.Combine(Paths.CachePath, "nx-request");
    private static readonly FieldInfo NxInternalSphereField = AccessTools.Field(typeof(SecretHistories.Tokens.Payloads.Nx), "_internalSphere");

    /// <summary>
    /// GameGateway.TryDefaultSave sets this, and when GameInSaveableState() says no (Wisdoms
    /// _saveAllowed false) it returns without clearing it: every later save - the autosave, the
    /// save on quit - is then refused until a restart. The game's own callers check
    /// GameInSaveableState() first; SaveNow does too, and clears the flag if it ever sticks.
    /// </summary>
    private static readonly FieldInfo SaveInProgressField = AccessTools.Field(typeof(GameGateway), "DefaultSaveInProgress");

    private void Awake()
    {
        Logger.LogInfo($"{Name} {Version} loaded");
        // An earlier version called TryDefaultSave without that check and left the flag stuck.
        ClearStuckSaveFlag();
        InvokeRepeating(nameof(CheckSaveRequest), 2f, 2f); // one File.Exists every 2 s
    }

    private void CheckSaveRequest()
    {
        CheckSpawnRequest();
        CheckRemoveRequest();
        CheckNxRequest();
        CheckRoomsRequest();
        if (!File.Exists(SaveRequestPath))
            return;
        try
        {
            File.Delete(SaveRequestPath);
        }
        catch (Exception)
        {
            return; // try again next time
        }
        SaveNow();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.ctrlKey.isPressed || !keyboard.shiftKey.isPressed)
            return;
        // Ctrl+Shift+S: the game's own default save (AUTOSAVE.json), so tools reading the save
        // (the vault's tree plan) see the current state without waiting for the 20-minute autosave.
        if (keyboard.sKey.wasPressedThisFrame)
        {
            SaveNow();
            return;
        }
        if (!keyboard.dKey.wasPressedThisFrame)
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

    private void CheckSpawnRequest()
    {
        if (!File.Exists(SpawnRequestPath))
            return;
        string[] ids;
        try
        {
            ids = File.ReadAllLines(SpawnRequestPath);
            File.Delete(SpawnRequestPath);
        }
        catch (Exception)
        {
            return;
        }
        var spheres = Watchman.Get<SecretHistories.Entities.HornedAxe>().GetSpheres().Where(sp => sp != null && !sp.Defunct).ToList();
        SecretHistories.Spheres.Sphere target = spheres.FirstOrDefault(sp => sp.GetAbsolutePath().ToString().EndsWith("hand.memories", StringComparison.Ordinal))
            ?? spheres.FirstOrDefault(sp => sp.GetAbsolutePath().ToString().Contains("hand."));
        foreach (string raw in ids)
        {
            string id = raw.Trim();
            if (id.Length == 0)
                continue;
            if (target == null)
            {
                Logger.LogWarning($"Spawn {id}: no hand sphere found");
                continue;
            }
            target.ProvisionElementToken(id, 1);
            Logger.LogInfo($"Spawned {id} into {target.GetAbsolutePath()}");
        }
    }

    private static string[] TakeRequest(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            string[] lines = File.ReadAllLines(path);
            File.Delete(path);
            return lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void CheckRemoveRequest()
    {
        string[] ids = TakeRequest(RemoveRequestPath);
        if (ids == null)
            return;
        var axe = Watchman.Get<SecretHistories.Entities.HornedAxe>();
        foreach (string id in ids)
        {
            SecretHistories.UI.Token token = axe.GetExteriorSpheres().Where(sp => sp != null && !sp.Defunct)
                .SelectMany(sp => sp.GetElementTokens())
                .FirstOrDefault(t => t.Payload is ElementStack stack && stack.EntityId == id);
            if (token == null)
            {
                Logger.LogWarning($"Remove {id}: not found");
                continue;
            }
            if (token.Quantity > 1)
                token.Payload.ModifyQuantity(-1);
            else
                token.Retire(SecretHistories.Enums.RetirementVFX.None);
            Logger.LogInfo($"Removed one {id} from {token.Sphere?.GetAbsolutePath()}");
        }
    }

    private void CheckNxRequest()
    {
        string[] lines = TakeRequest(NxRequestPath);
        if (lines == null)
            return;
        var nodes = Watchman.Get<SecretHistories.Spheres.NxNodesSphere>();
        foreach (string line in lines)
        {
            string[] parts = line.Split(new[] { ' ', '	' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
                continue;
            bool done = false;
            foreach (SecretHistories.UI.Token node in nodes.Tokens)
            {
                if (!(node.Payload is SecretHistories.Tokens.Payloads.Nx nx)
                    || !(NxInternalSphereField.GetValue(nx) is SecretHistories.Spheres.Sphere inner))
                    continue;
                SecretHistories.UI.Token story = inner.Tokens.FirstOrDefault(t => t.Payload is ElementStack s && s.EntityId == parts[0]);
                if (story == null)
                    continue;
                story.Retire(SecretHistories.Enums.RetirementVFX.None);
                inner.ProvisionElementToken(parts[1], 1);
                Logger.LogInfo($"Constellation {nx.Id}: {parts[0]} -> {parts[1]}");
                done = true;
            }
            if (!done)
                Logger.LogWarning($"Constellation story card {parts[0]} not found");
        }
    }

    private void ClearStuckSaveFlag()
    {
        if (!Watchman.Exists<GameGateway>() || SaveInProgressField == null)
            return;
        GameGateway gateway = Watchman.Get<GameGateway>();
        if (SaveInProgressField.GetValue(gateway) is true)
        {
            SaveInProgressField.SetValue(gateway, false);
            Logger.LogInfo("Cleared a stuck DefaultSaveInProgress flag");
        }
    }

    private async void SaveNow()
    {
        try
        {
            GameGateway gateway = Watchman.Get<GameGateway>();
            if (!gateway.GameInSaveableState())
            {
                Logger.LogInfo("Save skipped: the game isn't in a saveable state (the Tree of Wisdoms is open?)");
                return;
            }
            bool saved = await gateway.TryDefaultSave();
            Logger.LogInfo(saved ? "Saved (AUTOSAVE.json)" : "Save failed");
            ClearStuckSaveFlag();
        }
        catch (Exception e)
        {
            Logger.LogError(e);
        }
    }

    /// <summary>
    /// rooms-request: writes dumps/rooms.txt, "terrain id TAB label TAB unlocked TAB sprite names TAB
    /// shrouded=.. TAB sealed=..":
    /// the room background sprites (room_*) under each room token. Their names don't follow the
    /// terrain ids (terrain.ballroomduellinghall shows room_curia_duelling), so the vault's room
    /// pictures are matched through this.
    /// </summary>
    private static string RoomsRequestPath => Path.Combine(Paths.CachePath, "rooms-request");

    private void CheckRoomsRequest()
    {
        if (!File.Exists(RoomsRequestPath))
            return;
        try
        {
            File.Delete(RoomsRequestPath);
            var lines = new System.Collections.Generic.List<string>();
            foreach (SecretHistories.UI.Token token in Resources.FindObjectsOfTypeAll<SecretHistories.UI.Token>())
            {
                if (token == null || !token.gameObject.scene.IsValid() || !(token.Payload is SecretHistories.Tokens.Payloads.TerrainFeature room))
                    continue;
                var sprites = token.GetComponentsInChildren<Image>(true)
                    .Where(i => i.sprite != null && i.sprite.name.StartsWith("room_", StringComparison.Ordinal))
                    .Select(i => i.sprite.name).Distinct();
                // Shrouded: still dark, the player doesn't know it; sealed only: visible, waiting to be opened.
                lines.Add($"{room.Id}	{room.Label}	{!room.IsShrouded && !room.IsSealed}	{string.Join(",", sprites)}	shrouded={room.IsShrouded}	sealed={room.IsSealed}");
            }
            Directory.CreateDirectory(DumpDir);
            File.WriteAllLines(Path.Combine(DumpDir, "rooms.txt"), lines);
            Logger.LogInfo($"Wrote rooms.txt ({lines.Count} rooms)");
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
