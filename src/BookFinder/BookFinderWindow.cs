using BohShared;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using SecretHistories.Core;
using SecretHistories.Entities;
using SecretHistories.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BookFinder;

/// <summary>
/// The search window, built in code from the game's own sprites and fonts.
/// Two tabs share the search field and the result list; each has its own filters.
/// </summary>
public class BookFinderWindow : MonoBehaviour
{
    private enum Tab
    {
        Books,
        Memories,
        Items,
        People,
        Places,
    }

    private const int MaxRows = 300;

    private ManualLogSource _log;
    private RectTransform _root;
    private TMP_InputField _search;
    private TextMeshProUGUI _count;
    private RectTransform _results;
    private ScrollRect _scroll;

    private RectTransform _booksPanel;
    private TextMeshProUGUI _mysteryCaption;
    private RectTransform _levelRow;
    private RectTransform _languageGrid;
    private RectTransform _kindRow;
    private TextMeshProUGUI _contaminationCaption;
    private RectTransform _contaminationGrid;

    private RectTransform _memoriesPanel;
    /// <summary>Minimum principle levels a memory must have, e.g. moth 2 + heart 1.</summary>
    private readonly Dictionary<string, int> _memoryLevels = new Dictionary<string, int>();

    private RectTransform _itemsPanel;
    private TextMeshProUGUI _principleCaption;
    private RectTransform _tagGrid;

    private RectTransform _desksPanel;
    private TextMeshProUGUI _deskCaption;

    private RectTransform _peoplePanel;
    private TextMeshProUGUI _peopleCaption;

    private Tab _tab = Tab.Books;
    private string _query = "";
    private readonly BookFilter _bookFilter = new BookFilter();
    private readonly ItemFilter _itemFilter = new ItemFilter();
    private readonly PeopleFilter _peopleFilter = new PeopleFilter();
    private readonly DeskFilter _deskFilter = new DeskFilter();
    private readonly List<Chip> _chips = new List<Chip>();
    private readonly List<Chip> _bookChips = new List<Chip>();
    private readonly List<Chip> _tagChips = new List<Chip>();
    private List<ItemEntry> _items = new List<ItemEntry>();
    private List<PersonEntry> _people = new List<PersonEntry>();
    private List<DeskEntry> _desks = new List<DeskEntry>();
    private TokenMarker _marker;

    public bool IsOpen => gameObject.activeSelf;
    public bool IsSearchFocused => _search != null && _search.isFocused;

    public static BookFinderWindow Create(Transform canvasRoot, ManualLogSource log)
    {
        RectTransform root = GameStyle.Rect("BookFinderWindow", canvasRoot);
        root.gameObject.SetActive(false);
        var window = root.gameObject.AddComponent<BookFinderWindow>();
        window._log = log;
        window._root = root;
        window.Build();
        return window;
    }

    public bool IsPointerOver(Vector2 screenPoint)
    {
        return IsOpen && RectTransformUtility.RectangleContainsScreenPoint(_root, screenPoint, null);
    }

    public void Open()
    {
        gameObject.SetActive(true);
        _root.SetAsLastSibling();
        Rescan();
        StartCoroutine(FocusSearchNextFrame());
    }

    public void Close()
    {
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == _search.gameObject)
            EventSystem.current.SetSelectedGameObject(null);
        gameObject.SetActive(false);
    }

    private IEnumerator FocusSearchNextFrame()
    {
        // Next frame, so the key press that opened the window isn't typed into the field.
        yield return null;
        EventSystem.current?.SetSelectedGameObject(_search.gameObject);
        _search.ActivateInputField();
    }

    private void SwitchTab(Tab tab)
    {
        _tab = tab;
        _booksPanel.gameObject.SetActive(tab == Tab.Books);
        _memoriesPanel.gameObject.SetActive(tab == Tab.Memories);
        _itemsPanel.gameObject.SetActive(tab == Tab.Items);
        _peoplePanel.gameObject.SetActive(tab == Tab.People);
        _desksPanel.gameObject.SetActive(tab == Tab.Places);
        ((TextMeshProUGUI)_search.placeholder).text = tab == Tab.Books ? "Search books, records, films…"
            : tab == Tab.Memories ? "Search memories or books…"
            : tab == Tab.Items ? "Search items by name…"
            : tab == Tab.People ? "Search people by name…"
            : "Search places by name or room…";
        Refresh();
    }

    // ---------------------------------------------------------------- layout

    private void Build()
    {
        _root.anchorMin = _root.anchorMax = new Vector2(0f, 0.5f);
        _root.pivot = new Vector2(0f, 0.5f);
        _root.anchoredPosition = new Vector2(40f, 0f);
        _root.sizeDelta = new Vector2(640f, 860f);
        GameStyle.Layout<VerticalLayoutGroup>(_root, 0f, null, TextAnchor.UpperCenter).childForceExpandWidth = true;
        GameStyle.Background(_root, "slots_bg_outline", Color.white);
        GameStyle.Background(_root, "slot_bg_grey", new Color(1f, 1f, 1f, 0.95f)).raycastTarget = true;

        BuildHeader();

        RectTransform body = GameStyle.Rect("Body", _root);
        GameStyle.Layout<VerticalLayoutGroup>(body, 8f, new RectOffset(22, 22, 10, 20), TextAnchor.UpperLeft).childForceExpandWidth = true;
        GameStyle.Size(body).flexibleHeight = 1f;

        RectTransform tabs = GameStyle.Rect("Tabs", body);
        GameStyle.Layout<HorizontalLayoutGroup>(tabs, 8f);
        GameStyle.Size(tabs, height: 30f);
        foreach (Tab tab in new[] { Tab.Books, Tab.Memories, Tab.Items, Tab.People, Tab.Places })
        {
            Tab captured = tab;
            Chip chip = TextChip(tabs, tab.ToString(), 108f, () => _tab == captured, () => SwitchTab(captured));
            chip.Label.fontSize = 18f;
            _chips.Add(chip);
        }

        _search = GameStyle.InputField(body, "Search books by title…");
        GameStyle.Size(_search, height: 42f);
        _search.onValueChanged.AddListener(text =>
        {
            _query = text.Trim();
            Refresh();
        });

        BuildBooksPanel(body);
        BuildMemoriesPanel(body);
        _memoriesPanel.gameObject.SetActive(false);
        BuildItemsPanel(body);
        _itemsPanel.gameObject.SetActive(false);
        BuildPeoplePanel(body);
        _peoplePanel.gameObject.SetActive(false);
        BuildDesksPanel(body);
        _desksPanel.gameObject.SetActive(false);

        BuildResults(body);
    }

    private void BuildBooksPanel(Transform body)
    {
        _booksPanel = Panel(body, "BooksFilters");

        Caption(_booksPanel, "Type");
        _kindRow = Row(_booksPanel, "Kinds", 28f);

        _mysteryCaption = Caption(_booksPanel, "Mystery");
        RectTransform mysteries = Row(_booksPanel, "Mysteries", 40f);
        foreach (string principle in WorldIndex.Principles)
            AddPrincipleChip(mysteries, principle, _bookFilter.Mysteries, _mysteryCaption, "Mystery");

        Caption(_booksPanel, "Difficulty");
        _levelRow = Row(_booksPanel, "Levels", 28f);

        Caption(_booksPanel, "Status");
        RectTransform status = Row(_booksPanel, "Status", 28f);
        foreach (ReadState state in new[] { ReadState.Any, ReadState.Unread, ReadState.Mastered })
        {
            ReadState captured = state;
            _chips.Add(TextChip(status, state == ReadState.Any ? "All" : state.ToString(), 96f,
                () => _bookFilter.ReadState == captured,
                () => _bookFilter.ReadState = captured));
        }

        Caption(_booksPanel, "Language");
        _languageGrid = Grid(_booksPanel, "Languages", new Vector2(92f, 26f), 6);

        // Shown only while some book carries a contamination (see RebuildBookChips).
        _contaminationCaption = Caption(_booksPanel, "Contamination");
        _contaminationGrid = Grid(_booksPanel, "Contaminations", new Vector2(141f, 26f), 4);


    }

    private void BuildMemoriesPanel(Transform body)
    {
        _memoriesPanel = Panel(body, "MemoriesFilters");
        const string memoryText = "Memory with at least";
        TextMeshProUGUI caption = Caption(_memoriesPanel, memoryText);
        RectTransform row = Row(_memoriesPanel, "MemoryAspects", 40f);
        foreach (string principle in WorldIndex.Principles)
            AddLevelChip(row, principle, _memoryLevels, caption, memoryText);
    }

    private void BuildItemsPanel(Transform body)
    {
        _itemsPanel = Panel(body, "ItemsFilters");

        _principleCaption = Caption(_itemsPanel, "Principles, at least");
        RectTransform principles = Row(_itemsPanel, "Principles", 40f);
        foreach (string principle in WorldIndex.Principles)
            AddLevelChip(principles, principle, _itemFilter.Principles, _principleCaption, "Principles, at least");

        Caption(_itemsPanel, "Tags");
        // Tags can be many: a fixed-height scrolling box.
        RectTransform frame = GameStyle.Rect("TagsFrame", _itemsPanel);
        GameStyle.Size(frame, height: 190f); // ~6 rows; the rest scrolls
        // Invisible, but catches the wheel in the gaps between tags too.
        GameStyle.Background(frame, "", new Color(0f, 0f, 0f, 0f));
        RectTransform viewport = GameStyle.Rect("Viewport", frame);
        GameStyle.Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();
        GameStyle.Size(viewport).ignoreLayout = true;

        _tagGrid = GameStyle.Rect("Tags", viewport);
        _tagGrid.anchorMin = new Vector2(0f, 1f);
        _tagGrid.anchorMax = new Vector2(1f, 1f);
        _tagGrid.pivot = new Vector2(0.5f, 1f);
        _tagGrid.sizeDelta = Vector2.zero;
        // One row block per initial letter; see RebuildTagChips.
        GameStyle.Layout<VerticalLayoutGroup>(_tagGrid, 6f, null, TextAnchor.UpperLeft).childForceExpandWidth = true;
        _tagGrid.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = frame.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = _tagGrid;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        AddScrollbar(scroll, viewport, 0f);
        WheelStepper.Attach(scroll, 32f); // tag cell 26 + spacing 6
    }

    private void BuildPeoplePanel(Transform body)
    {
        _peoplePanel = Panel(body, "PeopleFilters");

        // Visitors match on their interests, assistants on the principles they help with.
        _peopleCaption = Caption(_peoplePanel, "Principles");
        RectTransform principles = Row(_peoplePanel, "Principles", 40f);
        foreach (string principle in WorldIndex.Principles)
            AddPrincipleChip(principles, principle, _peopleFilter.Principles, _peopleCaption, "Principles");

        Caption(_peoplePanel, "Who");
        RectTransform who = Row(_peoplePanel, "Who", 28f);
        _chips.Add(TextChip(who, "All", 96f, () => _peopleFilter.Kind == null, () => _peopleFilter.Kind = null));
        _chips.Add(TextChip(who, "Visitors", 96f, () => _peopleFilter.Kind == PersonKind.Visitor, () => _peopleFilter.Kind = PersonKind.Visitor));
        _chips.Add(TextChip(who, "Assistants", 96f, () => _peopleFilter.Kind == PersonKind.Assistant, () => _peopleFilter.Kind = PersonKind.Assistant));
        GameStyle.Size(GameStyle.Rect("Gap", who), 24f);
        _chips.Add(TextChip(who, "All met", 96f, () => !_peopleFilter.HereOnly, () => _peopleFilter.HereOnly = false));
        _chips.Add(TextChip(who, "Here now", 96f, () => _peopleFilter.HereOnly, () => _peopleFilter.HereOnly = true));
    }

    private void BuildDesksPanel(Transform body)
    {
        _desksPanel = Panel(body, "DesksFilters");

        _deskCaption = Caption(_desksPanel, "Works with");
        RectTransform principles = Row(_desksPanel, "Principles", 40f);
        foreach (string principle in WorldIndex.Principles)
            AddPrincipleChip(principles, principle, _deskFilter.Principles, _deskCaption, "Works with");

        Caption(_desksPanel, "Kind");
        RectTransform kinds = Row(_desksPanel, "Kinds", 28f);
        _chips.Add(TextChip(kinds, "All", 70f, () => _deskFilter.Kind == null, () => _deskFilter.Kind = null));
        foreach (PlaceKind kind in new[] { PlaceKind.Desk, PlaceKind.Bed, PlaceKind.House, PlaceKind.Village, PlaceKind.Outdoors })
        {
            PlaceKind captured = kind;
            _chips.Add(TextChip(kinds, KindLabel(kind), 86f, () => _deskFilter.Kind == captured, () => _deskFilter.Kind = captured));
        }

        Caption(_desksPanel, "State");
        RectTransform state = Row(_desksPanel, "State", 28f);
        _chips.Add(TextChip(state, "Any state", 96f, () => !_deskFilter.IdleOnly, () => _deskFilter.IdleOnly = false));
        _chips.Add(TextChip(state, "Idle only", 96f, () => _deskFilter.IdleOnly, () => _deskFilter.IdleOnly = true));
    }

    private void BuildHeader()
    {
        RectTransform header = GameStyle.Rect("Header", _root);
        GameStyle.Size(header, height: 52f);
        GameStyle.Background(header, "slots_bg_top", new Color(1f, 1f, 1f, 0.8f)).raycastTarget = true;
        header.gameObject.AddComponent<DragHandle>().Target = _root;

        TextMeshProUGUI title = GameStyle.Text(header, "Title", "Finder", 24f, GameStyle.Cream, FontStyles.Bold | FontStyles.SmallCaps);
        title.rectTransform.anchorMin = new Vector2(0f, 0f);
        title.rectTransform.anchorMax = new Vector2(1f, 1f);
        title.rectTransform.offsetMin = new Vector2(24f, 0f);
        title.rectTransform.offsetMax = new Vector2(-200f, 0f);

        _count = GameStyle.Text(header, "Count", "", 16f, GameStyle.Gold, FontStyles.Italic, TextAlignmentOptions.MidlineRight);
        _count.rectTransform.anchorMin = new Vector2(1f, 0f);
        _count.rectTransform.anchorMax = new Vector2(1f, 1f);
        _count.rectTransform.pivot = new Vector2(1f, 0.5f);
        _count.rectTransform.sizeDelta = new Vector2(160f, 0f);
        _count.rectTransform.anchoredPosition = new Vector2(-52f, 0f);

        Image close = GameStyle.Panel(header, "CloseButton", "window_x_large 1", Color.white);
        close.rectTransform.anchorMin = close.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        close.rectTransform.sizeDelta = new Vector2(24f, 24f);
        close.rectTransform.anchoredPosition = new Vector2(-24f, 0f);
        close.gameObject.AddComponent<Button>().onClick.AddListener(Close);
        GameStyle.Size(close).ignoreLayout = true;
    }

    private void BuildResults(Transform body)
    {
        RectTransform frame = GameStyle.Rect("Results", body);
        GameStyle.Size(frame).flexibleHeight = 1f;
        GameStyle.Size(frame).minHeight = 200f;
        GameStyle.Background(frame, "slots_bg", new Color(1f, 1f, 1f, 0.8f));

        RectTransform viewport = GameStyle.Rect("Viewport", frame);
        GameStyle.Stretch(viewport, 6, 6, 6, 6);
        viewport.gameObject.AddComponent<RectMask2D>();
        GameStyle.Size(viewport).ignoreLayout = true;

        _results = GameStyle.Rect("Content", viewport);
        _results.anchorMin = new Vector2(0f, 1f);
        _results.anchorMax = new Vector2(1f, 1f);
        _results.pivot = new Vector2(0.5f, 1f);
        _results.sizeDelta = Vector2.zero;
        GameStyle.Layout<VerticalLayoutGroup>(_results, 3f, null, TextAnchor.UpperLeft).childForceExpandWidth = true;
        _results.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _scroll = frame.gameObject.AddComponent<ScrollRect>();
        _scroll.viewport = viewport;
        _scroll.content = _results;
        _scroll.horizontal = false;
        _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 30f;
        AddScrollbar(_scroll, viewport, 6f);
        WheelStepper.Attach(_scroll, 57f); // result row 54 + spacing 3
    }

    /// <summary>
    /// Thin vertical scrollbar in the game's style (TerrainDetailWindow: dark track, light handle),
    /// on the right edge of the scroll area; hidden when everything fits.
    /// </summary>
    private static void AddScrollbar(ScrollRect scroll, RectTransform viewport, float inset)
    {
        const float width = 6f;
        RectTransform frame = (RectTransform)scroll.transform;
        Image track = GameStyle.Panel(frame, "Scrollbar", "Background", new Color(0f, 0f, 0f, 0.6f));
        RectTransform trackRect = track.rectTransform;
        trackRect.anchorMin = new Vector2(1f, 0f);
        trackRect.anchorMax = new Vector2(1f, 1f);
        trackRect.pivot = new Vector2(1f, 0.5f);
        trackRect.sizeDelta = new Vector2(width, -inset * 2f);
        trackRect.anchoredPosition = new Vector2(-inset * 0.5f, 0f);
        GameStyle.Size(track).ignoreLayout = true;

        RectTransform area = GameStyle.Rect("Sliding Area", trackRect);
        GameStyle.Stretch(area);
        Image handle = GameStyle.Panel(area, "Handle", "UISprite", GameStyle.Gold);
        GameStyle.Stretch(handle.rectTransform);

        Scrollbar bar = track.gameObject.AddComponent<Scrollbar>();
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.handleRect = handle.rectTransform;
        bar.targetGraphic = handle;
        ColorBlock colors = bar.colors;
        colors.highlightedColor = new Color(1.3f, 1.25f, 1.1f, 1f);
        bar.colors = colors;

        scroll.verticalScrollbar = bar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        // Keep content clear of the bar.
        viewport.offsetMax = new Vector2(viewport.offsetMax.x - width - 4f, viewport.offsetMax.y);
    }

    private static RectTransform Panel(Transform parent, string name)
    {
        RectTransform panel = GameStyle.Rect(name, parent);
        GameStyle.Layout<VerticalLayoutGroup>(panel, 8f, null, TextAnchor.UpperLeft).childForceExpandWidth = true;
        return panel;
    }

    private static RectTransform Row(Transform parent, string name, float height)
    {
        RectTransform row = GameStyle.Rect(name, parent);
        GameStyle.Layout<HorizontalLayoutGroup>(row, 6f);
        GameStyle.Size(row, height: height);
        return row;
    }

    private static RectTransform Grid(Transform parent, string name, Vector2 cell, int columns)
    {
        RectTransform rect = GameStyle.Rect(name, parent);
        var grid = rect.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = cell;
        grid.spacing = new Vector2(6f, 6f);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        return rect;
    }

    private static TextMeshProUGUI Caption(Transform parent, string text)
    {
        TextMeshProUGUI caption = GameStyle.Text(parent, "Caption", text, 16f, GameStyle.Gold, FontStyles.Bold | FontStyles.SmallCaps);
        GameStyle.Size(caption, height: 22f);
        return caption;
    }

    // ---------------------------------------------------------------- chips

    private class Chip
    {
        public GameObject Root;
        public Image Background;
        public Image Icon;
        public Image Glow;
        public Image Outline;
        public TextMeshProUGUI Label;
        public Func<bool> IsOn;

        public virtual void Update()
        {
            bool on = IsOn();
            if (Icon != null)
                Icon.color = on ? Color.white : new Color(1f, 1f, 1f, 0.35f);
            if (Glow != null)
                Glow.enabled = on;
            // "On" must read as lit up: bright border and text, not a filled (darker-looking) button.
            if (Outline != null)
                Outline.enabled = on;
            if (Background != null)
                Background.color = on ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            if (Label != null)
                Label.color = on ? GameStyle.Cream : GameStyle.GoldFaint;
        }
    }

    private void AddPrincipleChip(Transform parent, string principle, HashSet<string> selection, TextMeshProUGUI caption, string captionText)
    {
        RectTransform rect = GameStyle.Rect("Principle_" + principle, parent);
        GameStyle.Size(rect, 36f, 36f);

        Image glow = GameStyle.Panel(rect, "Glow", "card_shadow", GameStyle.Gold);
        GameStyle.Stretch(glow.rectTransform, -6, -6, -6, -6);
        glow.raycastTarget = false;

        Image icon = GameStyle.Panel(rect, "Icon", "", Color.white);
        icon.sprite = SpriteOf(principle);
        GameStyle.Stretch(icon.rectTransform);

        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = icon;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() =>
        {
            if (!selection.Remove(principle))
                selection.Add(principle);
            Refresh();
        });

        string label = WorldIndex.LabelOf(principle);
        var hover = rect.gameObject.AddComponent<HoverHandler>();
        hover.Enter = () => caption.text = captionText + ": " + label;
        hover.Exit = () => caption.text = captionText;
        _chips.Add(new Chip { Root = rect.gameObject, Icon = icon, Glow = glow, IsOn = () => selection.Contains(principle) });
    }

    /// <summary>
    /// Principle icon with a required level: left click +1, right click -1. The level is drawn
    /// on the icon; zero means "not required" and dims it.
    /// </summary>
    private void AddLevelChip(Transform parent, string principle, Dictionary<string, int> levels, TextMeshProUGUI caption, string captionText)
    {
        RectTransform rect = GameStyle.Rect("Level_" + principle, parent);
        GameStyle.Size(rect, 36f, 36f);

        Image glow = GameStyle.Panel(rect, "Glow", "card_shadow", GameStyle.Gold);
        GameStyle.Stretch(glow.rectTransform, -6, -6, -6, -6);
        glow.raycastTarget = false;
        Image icon = GameStyle.Panel(rect, "Icon", "", Color.white);
        icon.sprite = SpriteOf(principle);
        GameStyle.Stretch(icon.rectTransform);

        // Dark badge under the number for contrast. (Not TMP outline: setting it before the
        // window is first shown throws, the font material isn't set up yet.)
        Image badge = GameStyle.Panel(rect, "Badge", "box-rnd-16px", new Color(0f, 0f, 0f, 0.8f));
        RectTransform badgeRect = badge.rectTransform;
        badgeRect.anchorMin = badgeRect.anchorMax = new Vector2(1f, 0f);
        badgeRect.pivot = new Vector2(0.5f, 0.5f);
        badgeRect.sizeDelta = new Vector2(18f, 18f);
        badgeRect.anchoredPosition = new Vector2(-3f, 3f);
        badge.raycastTarget = false;
        TextMeshProUGUI value = GameStyle.Text(badge.transform, "Value", "", 15f, GameStyle.Cream, FontStyles.Bold, TextAlignmentOptions.Center);
        GameStyle.Stretch(value.rectTransform);

        string label = WorldIndex.LabelOf(principle);
        var clicks = rect.gameObject.AddComponent<ClickHandler>();
        clicks.Click = button =>
        {
            levels.TryGetValue(principle, out int level);
            level = Mathf.Clamp(level + (button == PointerEventData.InputButton.Right ? -1 : 1), 0, 20);
            if (level == 0)
                levels.Remove(principle);
            else
                levels[principle] = level;
            Refresh();
        };
        var hover = rect.gameObject.AddComponent<HoverHandler>();
        hover.Enter = () => caption.text = label + ": left click +1, right click -1";
        hover.Exit = () => caption.text = captionText;

        _chips.Add(new LevelChip(rect.gameObject, icon, glow, value, badge, () => levels.TryGetValue(principle, out int l) ? l : 0));
    }

    private class LevelChip : Chip
    {
        private readonly TextMeshProUGUI _value;
        private readonly Image _badge;
        private readonly Func<int> _level;

        public LevelChip(GameObject root, Image icon, Image glow, TextMeshProUGUI value, Image badge, Func<int> level)
        {
            Root = root;
            Icon = icon;
            Glow = glow;
            _value = value;
            _badge = badge;
            _level = level;
            IsOn = () => _level() > 0;
        }

        public override void Update()
        {
            base.Update();
            int level = _level();
            _value.text = level > 0 ? level.ToString() : "";
            _badge.enabled = level > 0;
        }
    }

    private Chip TextChip(Transform parent, string text, float width, Func<bool> isOn, Action toggle, Sprite icon = null)
    {
        Image bg = GameStyle.Panel(parent, "Chip_" + text, "slots_bg", Color.white);
        if (width > 0)
            GameStyle.Size(bg, width, 26f);
        Image outline = GameStyle.Panel(bg.transform, "Outline", "information_goldborder", GameStyle.BrightGold, 1.5f);
        GameStyle.Stretch(outline.rectTransform, -1, -1, -1, -1);
        outline.raycastTarget = false;

        Image iconImage = null;
        float textLeft = 4f;
        if (icon != null)
        {
            iconImage = GameStyle.Panel(bg.transform, "Icon", "", Color.white);
            iconImage.sprite = icon;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            RectTransform iconRect = iconImage.rectTransform;
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.sizeDelta = new Vector2(20f, 20f);
            iconRect.anchoredPosition = new Vector2(5f, 0f);
            textLeft = 27f;
        }
        TextMeshProUGUI label = GameStyle.Text(bg.transform, "Label", text, 15f, GameStyle.Gold, FontStyles.SmallCaps,
            icon != null ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center);
        GameStyle.Stretch(label.rectTransform, textLeft, 0, 4, 0);

        Button button = bg.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() =>
        {
            toggle();
            Refresh();
        });
        return new Chip { Root = bg.gameObject, Background = bg, Outline = outline, Label = label, Icon = iconImage, IsOn = isOn };
    }

    private static void Toggle<T>(HashSet<T> set, T value)
    {
        if (!set.Remove(value))
            set.Add(value);
    }

    /// <summary>Difficulty and language chips depend on which books exist, so they're rebuilt on rescan.</summary>
    private void RebuildBookChips()
    {
        foreach (Chip chip in _bookChips)
            Destroy(chip.Root);
        _bookChips.Clear();

        List<BookInfo> books = _items.Where(i => i.Book != null).Select(i => i.Book).ToList();
        string[] kindOrder = { "Book", "Record", "Film", "Scroll", "Tablet", "Letter", "Journal" };
        foreach (string kind in kindOrder.Where(k => books.Any(b => b.Kind == k)))
        {
            string captured = kind;
            _bookChips.Add(TextChip(_kindRow, kind + "s", 80f,
                () => _bookFilter.Kinds.Contains(captured),
                () => Toggle(_bookFilter.Kinds, captured)));
        }
        foreach (int level in books.Where(b => b.Mystery != null).Select(b => b.Level).Distinct().OrderBy(l => l))
        {
            int captured = level;
            _bookChips.Add(TextChip(_levelRow, level.ToString(), 44f,
                () => _bookFilter.Levels.Contains(captured),
                () => Toggle(_bookFilter.Levels, captured)));
        }

        List<string> contaminations = books.SelectMany(b => b.Contaminations).Distinct()
            .OrderBy(WorldIndex.LabelOf, StringComparer.OrdinalIgnoreCase).ToList();
        bool anyInfected = contaminations.Count > 0;
        _contaminationCaption.gameObject.SetActive(anyInfected);
        _contaminationGrid.gameObject.SetActive(anyInfected);
        if (!anyInfected)
            _bookFilter.Contaminations.Clear(); // a hidden filter must not keep hiding books
        else
        {
            _bookChips.Add(TextChip(_contaminationGrid, "Clean", -1f,
                () => _bookFilter.Contaminations.Contains(BookFilter.NoContamination),
                () => Toggle(_bookFilter.Contaminations, BookFilter.NoContamination)));
            _bookChips.Add(TextChip(_contaminationGrid, "Any", -1f,
                () => _bookFilter.Contaminations.Contains(BookFilter.AnyContamination),
                () => Toggle(_bookFilter.Contaminations, BookFilter.AnyContamination)));
            foreach (string contamination in contaminations)
            {
                string captured = contamination;
                _bookChips.Add(TextChip(_contaminationGrid, WorldIndex.LabelOf(contamination), -1f,
                    () => _bookFilter.Contaminations.Contains(captured),
                    () => Toggle(_bookFilter.Contaminations, captured),
                    SpriteOf(contamination)));
            }
        }

        IEnumerable<string> languages = books.Select(b => b.Language ?? WorldIndex.NoLanguage).Distinct()
            .OrderBy(id => id == WorldIndex.NoLanguage ? "" : WorldIndex.LanguageName(id));
        foreach (string language in languages)
        {
            string captured = language;
            bool native = language == WorldIndex.NoLanguage;
            _bookChips.Add(TextChip(_languageGrid, native ? "Native" : WorldIndex.LanguageName(language), -1f,
                () => _bookFilter.Languages.Contains(captured),
                () => Toggle(_bookFilter.Languages, captured),
                native ? null : SpriteOf(language)));
        }
    }

    /// <summary>
    /// Faceted tags: all tags carried by the items currently matching, so the list narrows as
    /// filters are added. Grouped by initial letter, each group starting a new line with its
    /// letter in the margin; selected tags come first, marked "•". (A cap on the most common
    /// tags used to hide rare ones like Ink until another filter thinned the list.)
    /// </summary>
    private void RebuildTagChips(List<ItemEntry> matches)
    {
        _tagChips.Clear();
        for (int i = _tagGrid.childCount - 1; i >= 0; i--)
            Destroy(_tagGrid.GetChild(i).gameObject);

        var counts = new Dictionary<string, int>();
        foreach (ItemEntry item in matches)
        {
            foreach (string aspect in item.Aspects.Keys)
            {
                if (item.Aspects[aspect] > 0 && WorldIndex.IsTag(aspect))
                    counts[aspect] = counts.TryGetValue(aspect, out int n) ? n + 1 : 1;
            }
        }

        var groups = new List<(string mark, List<string> tags)>();
        if (_itemFilter.Tags.Count > 0)
            groups.Add(("•", _itemFilter.Tags.OrderBy(WorldIndex.LabelOf, StringComparer.OrdinalIgnoreCase).ToList()));
        groups.AddRange(counts.Keys.Where(t => !_itemFilter.Tags.Contains(t))
            .OrderBy(WorldIndex.LabelOf, StringComparer.OrdinalIgnoreCase)
            .GroupBy(t => char.ToUpperInvariant(WorldIndex.LabelOf(t).FirstOrDefault()))
            .Select(g => (g.Key.ToString(), g.ToList())));

        foreach ((string mark, List<string> tags) in groups)
        {
            RectTransform block = GameStyle.Rect("Letter_" + mark, _tagGrid);
            GameStyle.Layout<HorizontalLayoutGroup>(block, 6f, null, TextAnchor.UpperLeft);
            GameStyle.Size(GameStyle.Text(block, "Letter", mark, 18f, GameStyle.Gold, FontStyles.Bold,
                TextAlignmentOptions.Center), 18f, 26f);

            RectTransform grid = GameStyle.Rect("Tags", block);
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(136f, 26f);
            layout.spacing = new Vector2(6f, 6f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 4;
            GameStyle.Size(grid, flexibleWidth: 1f);

            foreach (string tag in tags)
            {
                string captured = tag;
                string label = WorldIndex.LabelOf(tag) + (counts.TryGetValue(tag, out int n) ? $" ({n})" : "");
                Chip chip = TextChip(grid, label, -1f,
                    () => _itemFilter.Tags.Contains(captured),
                    () => Toggle(_itemFilter.Tags, captured),
                    SpriteOf(tag));
                chip.Update();
                _tagChips.Add(chip);
            }
        }
    }

    private static Sprite SpriteOf(Element element) =>
        element != null ? ResourcesManager.GetAppropriateSpriteForElement(element) : null;

    private static Sprite SpriteOf(string elementId)
    {
        Element element = WorldIndex.ElementOf(elementId);
        return element != null ? ResourcesManager.GetAppropriateSpriteForElement(element) : null;
    }

    // ---------------------------------------------------------------- results

    private void Rescan()
    {
        try
        {
            var report = new List<string>();
            _items = WorldIndex.Scan(report).OrderBy(i => i.Title).ToList();
            // Debug: every tag the Items tab can offer, with how many items carry it.
            report.AddRange(_items.Where(i => i.Book == null)
                .SelectMany(i => i.Aspects.Where(a => a.Value > 0 && WorldIndex.IsTag(a.Key)).Select(a => a.Key))
                .GroupBy(t => t)
                .Select(g => $"TAG | {WorldIndex.LabelOf(g.Key)} ({g.Key}) x{g.Count()}"));
            WriteReport(report);
            _people = PeopleIndex.Scan();
            _desks = DeskIndex.Scan();
        }
        catch (Exception e)
        {
            _log.LogError(e);
            _items = new List<ItemEntry>();
            _people = new List<PersonEntry>();
            _desks = new List<DeskEntry>();
        }
        RebuildBookChips();
        Refresh();
    }

    /// <summary>Every token found and why it was shown or skipped: BepInEx\dumps\finder.txt.</summary>
    private void WriteReport(List<string> report)
    {
        try
        {
            string dir = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "dumps");
            System.IO.Directory.CreateDirectory(dir);
            report.Sort(StringComparer.Ordinal);
            System.IO.File.WriteAllLines(System.IO.Path.Combine(dir, "finder.txt"), report);
        }
        catch (Exception e)
        {
            _log.LogWarning("Can't write finder report: " + e.Message);
        }
    }

    private void Refresh()
    {
        foreach (Chip chip in _chips.Concat(_bookChips))
            chip.Update();

        for (int i = _results.childCount - 1; i >= 0; i--)
            Destroy(_results.GetChild(i).gameObject);

        List<ItemEntry> matches;
        int total;
        if (_tab == Tab.Books)
        {
            total = _items.Count(i => i.Book != null);
            // Easiest first; books without a mystery (no difficulty) last.
            matches = _items.Where(i => _bookFilter.Matches(i, _query))
                .OrderBy(i => i.Book.Mystery != null ? i.Book.Level : int.MaxValue)
                .ThenBy(i => i.Title)
                .ToList();
            foreach (ItemEntry item in matches.Take(MaxRows))
                AddBookRow(item);
        }
        else if (_tab == Tab.Memories)
        {
            RefreshMemories();
            _scroll.verticalNormalizedPosition = 1f;
            return;
        }
        else if (_tab == Tab.Places)
        {
            total = _desks.Count;
            List<DeskEntry> desks = _desks.Where(d => _deskFilter.Matches(d, _query)).ToList();
            foreach (DeskEntry desk in desks.Take(MaxRows))
                AddDeskRow(desk);
            _count.text = $"{desks.Count} of {total}";
            _scroll.verticalNormalizedPosition = 1f;
            return;
        }
        else if (_tab == Tab.People)
        {
            total = _people.Count;
            List<PersonEntry> people = _people.Where(p => _peopleFilter.Matches(p, _query)).ToList();
            foreach (PersonEntry person in people.Take(MaxRows))
                AddPersonRow(person);
            _count.text = $"{people.Count} of {total} met";
            _scroll.verticalNormalizedPosition = 1f;
            return;
        }
        else
        {
            // Readables (books, records, films) have their own tab.
            List<ItemEntry> things = _items.Where(i => i.Book == null).ToList();
            total = things.Count;
            matches = things.Where(i => _itemFilter.Matches(i, _query))
                .OrderByDescending(_itemFilter.Score)
                .ThenBy(i => i.Title)
                .ToList();
            RebuildTagChips(matches);
            foreach (ItemEntry item in matches.Take(MaxRows))
                AddItemRow(item);
        }

        _count.text = matches.Count > MaxRows ? $"{MaxRows} of {matches.Count}" : $"{matches.Count} of {total}";
        _scroll.verticalNormalizedPosition = 1f;
    }

    /// <summary>Clickable row with the item's art and two text lines; returns the row for extra columns.</summary>
    private Transform AddRow(ItemEntry item, string subtitle)
    {
        string title = item.Stack.Quantity > 1 ? $"{item.Title} ×{item.Stack.Quantity}" : item.Title;
        return AddRow(SpriteOf(item.Stack.Element), title, subtitle + (item.InSecretCompartment ? (subtitle.Length > 0 ? " · " : "") + "secret compartment" : ""),
            () => Focus(item.Token, item.Stack, item.Title));
    }

    private Transform AddRow(Sprite artSprite, string title, string subtitle, Action onClick, int indent = 0)
    {
        Image row = GameStyle.Panel(_results, "Row", "slots_bg", new Color(1f, 1f, 1f, indent > 0 ? 0.25f : 0.45f));
        GameStyle.Size(row, height: 54f);
        GameStyle.Layout<HorizontalLayoutGroup>(row, 10f, new RectOffset(8 + indent, 12, 4, 4));
        Button button = row.gameObject.AddComponent<Button>();
        button.targetGraphic = row;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.6f, 1.4f, 1f, 1f);
        colors.pressedColor = new Color(2f, 1.7f, 1.1f, 1f);
        button.colors = colors;
        button.onClick.AddListener(() => onClick());

        Image art = GameStyle.Panel(row.transform, "Art", "", Color.white);
        art.sprite = artSprite;
        art.preserveAspect = true;
        art.raycastTarget = false;
        GameStyle.Size(art, 40f, 46f);

        RectTransform texts = GameStyle.Rect("Texts", row.transform);
        GameStyle.Layout<VerticalLayoutGroup>(texts, 0f, null, TextAnchor.MiddleLeft).childForceExpandWidth = true;
        GameStyle.Size(texts, flexibleWidth: 1f);
        GameStyle.Text(texts, "Title", title, 18f, GameStyle.Cream);
        GameStyle.Text(texts, "Where", subtitle, 14f, GameStyle.GoldFaint, FontStyles.Italic);
        return row.transform;
    }

    private void AddBookRow(ItemEntry item)
    {
        BookInfo book = item.Book;
        string where = item.Location;
        if (book.Kind != "Book")
            where = book.Kind + (where.Length > 0 ? " · " + where : "");
        if (book.Language != null)
            where = WorldIndex.LanguageName(book.Language) + (where.Length > 0 ? " · " + where : "");
        Transform row = AddRow(item, where);

        foreach (string contamination in book.Contaminations)
        {
            Image warning = GameStyle.Panel(row, "Contamination", "", Color.white);
            warning.sprite = SpriteOf(contamination);
            warning.preserveAspect = true;
            warning.raycastTarget = false;
            GameStyle.Size(warning, 24f, 24f);
        }
        if (book.Mystery != null)
            AspectBadge(row, book.Mystery, book.Level, 28f, 20f);

        // Mastered: the game's own check mark ("slot yes"); keeps room for long titles.
        Image mastered = GameStyle.Panel(row, "Mastered", "slot yes", Color.white);
        mastered.preserveAspect = true;
        mastered.raycastTarget = false;
        mastered.enabled = book.Mastered;
        GameStyle.Size(mastered, 22f, 22f);
    }

    private void AddItemRow(ItemEntry item)
    {
        Transform row = AddRow(item, item.Location);
        // Selected principles first, then the strongest ones.
        IEnumerable<KeyValuePair<string, int>> principles = item.Aspects
            .Where(a => a.Value > 0 && WorldIndex.IsPrinciple(a.Key))
            .OrderByDescending(a => _itemFilter.Principles.ContainsKey(a.Key))
            .ThenByDescending(a => a.Value)
            .Take(5);
        foreach (KeyValuePair<string, int> principle in principles)
            AspectBadge(row, principle.Key, principle.Value, 22f, 16f);
    }

    /// <summary>
    /// Memories that mastered books give on rereading, each followed by the books giving it.
    /// Only mastered books count (BookInfo.Memory), so nothing unseen is listed.
    /// </summary>
    private void RefreshMemories()
    {
        var byMemory = _items.Where(i => i.Book?.Memory != null)
            .GroupBy(i => i.Book.Memory)
            .Select(g => (memory: g.Key, element: WorldIndex.ElementOf(g.Key), books: g.OrderBy(i => i.Title).ToList()))
            .Where(m => m.element != null)
            .ToList();
        int total = byMemory.Count;

        var shown = byMemory
            .Where(m => _memoryLevels.All(r => (m.element.Aspects?.AspectValue(r.Key) ?? 0) >= r.Value))
            .Where(m => string.IsNullOrEmpty(_query)
                || WorldIndex.LabelOf(m.memory).IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0
                || m.books.Any(b => WorldIndex.TitleMatches(b, _query)))
            .OrderByDescending(m => _memoryLevels.Keys.Sum(p => m.element.Aspects?.AspectValue(p) ?? 0))
            .ThenBy(m => WorldIndex.LabelOf(m.memory), StringComparer.OrdinalIgnoreCase)
            .ToList();

        int rows = 0;
        foreach ((string memory, Element element, List<ItemEntry> books) in shown)
        {
            if (rows >= MaxRows)
                break;
            string label = WorldIndex.LabelOf(memory);
            string subtitle = books.Count == 1 ? "From 1 book" : $"From {books.Count} books";
            Transform row = AddRow(SpriteOf(element), label, subtitle, () =>
                Watchman.Get<AureateNotifier>().ShowAspectDetails(element, SpriteOf(element)));
            if (element.Aspects != null)
            {
                foreach (KeyValuePair<string, int> aspect in element.Aspects
                             .Where(a => a.Value > 0 && WorldIndex.IsPrinciple(a.Key))
                             .OrderByDescending(a => _memoryLevels.ContainsKey(a.Key))
                             .ThenByDescending(a => a.Value)
                             .Take(5))
                    AspectBadge(row, aspect.Key, aspect.Value, 22f, 16f);
            }
            rows++;
            foreach (ItemEntry book in books)
            {
                AddRow(SpriteOf(book.Stack.Element), book.Title, book.Location,
                    () => Focus(book.Token, book.Stack, book.Title), indent: 40);
                rows++;
            }
        }
        _count.text = $"{shown.Count} of {total}";
    }

    private void AddPersonRow(PersonEntry person)
    {
        string where = person.Here ? "Here" + (person.Location.Length > 0 ? ": " + person.Location : "") : "Away";
        if (person.Kind == PersonKind.Assistant)
            where = "Assistant · " + where;
        string languages = string.Join(", ", person.Languages);
        if (languages.Length > 0)
            where += " · Reads " + languages;
        Transform row = AddRow(SpriteOf(person.Element), person.Name, where, () =>
        {
            if (person.Here && person.Token != null)
                Focus(person.Token, person.Stack, person.Name);
            else if (person.Stack != null)
                ShowDetails(person.Stack, person.Name);
            else
                // A departed assistant has no card left: show the element itself.
                Watchman.Get<AureateNotifier>().ShowAspectDetails(person.Element, ResourcesManager.GetAppropriateSpriteForElement(person.Element));
        });
        IEnumerable<KeyValuePair<string, int>> principles = person.Principles
            .OrderByDescending(p => _peopleFilter.Principles.Contains(p.Key))
            .ThenByDescending(p => p.Value)
            .Take(5);
        foreach (KeyValuePair<string, int> principle in principles)
            AspectBadge(row, principle.Key, principle.Value, 22f, 16f);
    }

    private static string KindLabel(PlaceKind kind) =>
        kind == PlaceKind.Desk ? "Desks" : kind == PlaceKind.Bed ? "Beds" : kind.ToString();

    private void AddDeskRow(DeskEntry desk)
    {
        string subtitle = KindLabel(desk.Kind) + " · "
            + (desk.Location.Length > 0 ? desk.Location + " · " : "") + desk.StateLabel;
        if (desk.Extras.Count > 0)
            subtitle += " · " + string.Join(" · ", desk.Extras);
        Sprite art = null;
        try
        {
            art = desk.Situation.GetDetailSprite();
        }
        catch (Exception)
        {
            // Some verbs have no large icon.
        }
        Transform row = AddRow(art, desk.Name, subtitle, () => Focus(desk.Token, desk.Situation, desk.Name));
        // Selected principles first; desks have no values, just the four they work with.
        foreach (string principle in desk.Principles.OrderByDescending(_deskFilter.Principles.Contains))
        {
            Image icon = GameStyle.Panel(row, "Principle_" + principle, "", Color.white);
            icon.sprite = SpriteOf(principle);
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            GameStyle.Size(icon, 24f, 24f);
        }
    }

    private static void AspectBadge(Transform row, string aspectId, int value, float iconSize, float fontSize)
    {
        Image icon = GameStyle.Panel(row, "Aspect_" + aspectId, "", Color.white);
        icon.sprite = SpriteOf(aspectId);
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        GameStyle.Size(icon, iconSize, iconSize);
        GameStyle.Size(GameStyle.Text(row, "Value", value.ToString(), fontSize, GameStyle.Cream, FontStyles.Bold), fontSize + 6f);
    }

    private void Focus(Token token, IDetailable details, string title)
    {
        if (token == null || !token.IsValid())
        {
            Rescan();
            return;
        }
        if (IsInWorld(token))
            Watchman.Get<CamOperator>().PointAtTableLevelAtQuiteCloseHeight(token.transform.position, 0.6f, null);
        StartCoroutine(Pulse(token));
        // Just under the window, so the frame never covers it.
        _marker = TokenMarker.Show(token, (RectTransform)_root.parent, _marker);
        _marker.transform.SetSiblingIndex(_root.GetSiblingIndex());
        ShowDetails(details, title);
    }

    private void ShowDetails(IDetailable details, string title)
    {
        try
        {
            Watchman.Get<AureateNotifier>().ShowDetailable(details);
        }
        catch (Exception e)
        {
            _log.LogDebug($"Can't show details of {title}: {e.Message}");
        }
    }

    /// <summary>
    /// False for tokens drawn on a screen-space canvas (hand, Belongings trays): their position
    /// isn't a point on the map, and pointing the camera at it sends it off the board.
    /// </summary>
    private static bool IsInWorld(Token token)
    {
        Canvas canvas = token.GetComponentInParent<Canvas>();
        return canvas == null || canvas.rootCanvas.renderMode == RenderMode.WorldSpace;
    }

    private static IEnumerator Pulse(Token token)
    {
        yield return new WaitForSecondsRealtime(0.5f);
        for (int i = 0; i < 3 && token != null; i++)
        {
            token.AttentionPls();
            yield return new WaitForSecondsRealtime(0.9f);
        }
    }
}

/// <summary>
/// Exactly one step (a row) per mouse-wheel notch. ScrollRect's own wheel handling scales the
/// raw scroll delta, which varies by system and felt far too fast, so it's switched off.
/// </summary>
public class WheelStepper : MonoBehaviour, IScrollHandler
{
    private ScrollRect _scroll;
    private float _step;

    public static void Attach(ScrollRect scroll, float step)
    {
        scroll.scrollSensitivity = 0f;
        var stepper = scroll.gameObject.AddComponent<WheelStepper>();
        stepper._scroll = scroll;
        stepper._step = step;
    }

    public void OnScroll(PointerEventData eventData)
    {
        float direction = Mathf.Sign(eventData.scrollDelta.y);
        if (eventData.scrollDelta.y == 0f)
            return;
        RectTransform content = _scroll.content;
        float max = Mathf.Max(0f, content.rect.height - _scroll.viewport.rect.height);
        Vector2 position = content.anchoredPosition;
        // Wheel up (positive) moves the content down, back towards the top.
        position.y = Mathf.Clamp(position.y - direction * _step, 0f, max);
        _scroll.StopMovement();
        content.anchoredPosition = position;
    }
}

public class DragHandle : MonoBehaviour, IDragHandler
{
    public RectTransform Target;

    public void OnDrag(PointerEventData eventData)
    {
        Canvas canvas = Target.GetComponentInParent<Canvas>();
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        Target.anchoredPosition += eventData.delta / scale;
    }
}

public class ClickHandler : MonoBehaviour, IPointerClickHandler
{
    public Action<PointerEventData.InputButton> Click;

    public void OnPointerClick(PointerEventData eventData) => Click?.Invoke(eventData.button);
}

public class HoverHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Action Enter;
    public Action Exit;

    public void OnPointerEnter(PointerEventData eventData) => Enter?.Invoke();
    public void OnPointerExit(PointerEventData eventData) => Exit?.Invoke();
}
