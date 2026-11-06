using BepInEx;
using HarmonyLib;
using SecretHistories.Infrastructure;
using UnityEngine;

namespace CopyBreaks;

/// <summary>
/// Clicking a description copies it to the clipboard; this appends two line breaks, so texts
/// pasted one after another into a translator come out as separate paragraphs. Every copy in
/// the game goes through ClipboardTextCopier.CopyTextToClipboard(string) (the other overloads
/// format label + description and call it), which sets GUIUtility.systemCopyBuffer first.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class CopyBreaksPlugin : BaseUnityPlugin
{
    public const string Guid = "shirofaii.boh.copybreaks";
    public const string Name = "Copy Breaks";
    public const string Version = "0.1.0";

    private Harmony _harmony;

    private void Awake()
    {
        _harmony = new Harmony(Guid);
        _harmony.PatchAll(typeof(AppendBreaks));
        _harmony.PatchAll(typeof(ReadingCopyButton.WindowPatch));
        _harmony.PatchAll(typeof(SalonCopyButton.WindowPatch));
        StartCoroutine(ReadingCopyButton.FillOpenWindows());
        StartCoroutine(SalonCopyButton.FillOpenWindows());
        Logger.LogInfo($"{Name} {Version} loaded");
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        ReadingCopyButton.RemoveAll();
    }

    [HarmonyPatch(typeof(ClipboardTextCopier), nameof(ClipboardTextCopier.CopyTextToClipboard), typeof(string))]
    private static class AppendBreaks
    {
        private static void Postfix()
        {
            // One break before and one after: Google Translate drops all but one trailing break,
            // so two at the end left the texts only a line apart there; a leading break survives.
            string copied = GUIUtility.systemCopyBuffer;
            if (!string.IsNullOrEmpty(copied) && !(copied.StartsWith("\r\n") && copied.EndsWith("\r\n")))
                GUIUtility.systemCopyBuffer = "\r\n" + copied.Trim('\r', '\n') + "\r\n";
        }
    }
}
