using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BohShared;
using SecretHistories.Core;
using SecretHistories.Entities;
using SecretHistories.Spheres;
using SecretHistories.Tokens.Payloads;
using SecretHistories.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RoomHints;

/// <summary>
/// Draws what it takes to open a locked room right on the room, so it isn't necessary to click
/// it first. The requirements are the first slot of the room's info recipe
/// (TerrainFeature.GetInfoRecipe().PreSlots[0]): Essential (all of), Required (one of) and
/// Forbidden aspects, the same data TerrainDetailWindow shows. Only rooms the room window
/// would offer are marked: ConnectedTerrain that is shrouded but not sealed.
/// </summary>
[BepInPlugin(Guid, Name, Version)]
public class RoomHintsPlugin : BaseUnityPlugin
{
    public const string Guid = "shirofaii.boh.roomhints";
    public const string Name = "Room Hints";
    public const string Version = "0.1.0";

    private readonly Dictionary<ConnectedTerrain, GameObject> _hints = new Dictionary<ConnectedTerrain, GameObject>();
    /// <summary>Locked rooms whose requirements are empty (after omissions): nothing to draw.</summary>
    private readonly HashSet<ConnectedTerrain> _noHint = new HashSet<ConnectedTerrain>();
    /// <summary>All rooms; they're fixed scene objects, so the full sphere scan runs rarely.</summary>
    private readonly List<ConnectedTerrain> _rooms = new List<ConnectedTerrain>();
    private float _nextCheck;
    private float _nextRoomScan;

    private void Awake()
    {
        Logger.LogInfo($"{Name} {Version} loaded");
    }

    private void OnDestroy()
    {
        // ScriptEngine destroys the plugin on hot reload.
        foreach (GameObject hint in _hints.Values)
        {
            if (hint != null)
                Destroy(hint);
        }
        _hints.Clear();
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextCheck)
            return;
        _nextCheck = Time.unscaledTime + 2f;
        if (!Watchman.Exists<HornedAxe>())
            return;
        try
        {
            Refresh();
        }
        catch (Exception e)
        {
            Logger.LogError(e);
        }
    }

    private void Refresh()
    {
        if (_rooms.Count == 0 || Time.unscaledTime > _nextRoomScan || _rooms.Exists(r => r == null))
        {
            _nextRoomScan = Time.unscaledTime + 30f;
            _rooms.Clear();
            foreach (Sphere sphere in Watchman.Get<HornedAxe>().GetSpheres())
            {
                if (sphere == null || sphere.Defunct)
                    continue;
                foreach (Token token in sphere.Tokens)
                {
                    if (token != null && token.Payload is ConnectedTerrain room)
                        _rooms.Add(room);
                }
            }
        }

        var lockedRooms = new HashSet<ConnectedTerrain>();
        foreach (ConnectedTerrain room in _rooms)
        {
            if (room != null && room.IsShrouded && !room.IsSealed)
                lockedRooms.Add(room);
        }

        foreach (ConnectedTerrain room in _hints.Keys.ToList())
        {
            if (room == null || !lockedRooms.Contains(room) || _hints[room] == null)
            {
                if (_hints[room] != null)
                    Destroy(_hints[room]);
                _hints.Remove(room);
            }
        }
        _noHint.RemoveWhere(room => room == null || !lockedRooms.Contains(room));
        foreach (ConnectedTerrain room in lockedRooms)
        {
            if (_hints.ContainsKey(room) || _noHint.Contains(room))
                continue;
            GameObject hint = BuildHint(room);
            if (hint != null)
                _hints[room] = hint;
            else
                _noHint.Add(room);
        }
    }

    /// <summary>Needed by every room ("assistance": someone from the village), so not worth showing.</summary>
    private static readonly HashSet<string> Omitted = new HashSet<string> { "assistance" };

    private GameObject BuildHint(ConnectedTerrain room)
    {
        SphereSpec slot = room.GetInfoRecipe()?.PreSlots?.FirstOrDefault();
        if (slot == null || room.Token == null || !(room.Token.transform is RectTransform roomRect))
            return null;

        // Just the requirements, no group marks; forbidden ones (rare) are tinted red.
        var groups = new List<(bool forbidden, List<KeyValuePair<string, int>> aspects)>
        {
            (false, Visible(slot.Essential)),
            (false, Visible(slot.Required)),
            (true, Visible(slot.Forbidden)),
        };
        int rows = groups.Sum(g => g.aspects.Count);
        if (rows == 0)
            return null;

        // A column to the right of the unlock icon in the middle of the room, sized so that
        // all rows fit the room's height. Units are the room's own (world canvas) units.
        float height = roomRect.rect.height;
        float unit = Mathf.Clamp(Mathf.Min(height * 0.16f, height * 0.75f / (rows * 1.2f)), 10f, 60f);

        RectTransform hint = GameStyle.Rect("RoomHints", roomRect);
        hint.anchorMin = hint.anchorMax = new Vector2(0.5f, 0.5f);
        hint.pivot = new Vector2(0f, 0.5f);
        hint.anchoredPosition = new Vector2(height * 0.3f, 0f);
        hint.SetAsLastSibling(); // above the shroud
        var group = hint.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false; // clicks still open the room window
        group.interactable = false;

        int pad = Mathf.RoundToInt(unit * 0.3f);
        GameStyle.Layout<VerticalLayoutGroup>(hint, unit * 0.2f, new RectOffset(pad, pad, pad, pad), TextAnchor.MiddleLeft);
        var fitter = hint.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        GameStyle.Background(hint, "slots_bg", new Color(1f, 1f, 1f, 0.9f));

        foreach ((bool forbidden, List<KeyValuePair<string, int>> aspects) in groups)
        {
            foreach (KeyValuePair<string, int> aspect in aspects)
                AddRow(hint, aspect, forbidden, unit);
        }

        foreach (Graphic graphic in hint.GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;
        return hint.gameObject;
    }

    private static List<KeyValuePair<string, int>> Visible(AspectsDictionary aspects)
    {
        if (aspects == null)
            return new List<KeyValuePair<string, int>>();
        return aspects.Where(a => !Omitted.Contains(a.Key)).OrderByDescending(a => a.Value).ToList();
    }

    /// <summary>[aspect icon] [value if above 1]</summary>
    private static void AddRow(RectTransform parent, KeyValuePair<string, int> aspect, bool forbidden, float unit)
    {
        RectTransform row = GameStyle.Rect("Row_" + aspect.Key, parent);
        GameStyle.Layout<HorizontalLayoutGroup>(row, unit * 0.25f, null, TextAnchor.MiddleLeft);
        GameStyle.Size(row, height: unit);

        Element element = Watchman.Get<Compendium>().GetEntityById<Element>(aspect.Key);
        Image icon = GameStyle.Panel(row, "Aspect", "", forbidden ? new Color(1f, 0.45f, 0.45f, 1f) : Color.white);
        icon.sprite = element != null ? ResourcesManager.GetAppropriateSpriteForElement(element) : null;
        icon.preserveAspect = true;
        GameStyle.Size(icon, unit, unit);

        TextMeshProUGUI value = GameStyle.Text(row, "Value", aspect.Value > 1 ? aspect.Value.ToString() : "", unit * 0.75f, GameStyle.Cream, FontStyles.Bold);
        GameStyle.Size(value, unit * 0.9f, unit);
    }
}
