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
        Cures,
        Inks,
        Souls,
        Cookbook,
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
    /// <summary>Books tab: visitors in the house, to pick books one of them can be given.</summary>
    private TextMeshProUGUI _guestCaption;
    private RectTransform _guestRow;
    private RectTransform _requestRow;
    private TextMeshProUGUI _contaminationCaption;
    private RectTransform _contaminationGrid;

    private RectTransform _memoriesPanel;
    /// <summary>Minimum principle levels a memory must have, e.g. moth 2 + heart 1.</summary>
    private readonly Dictionary<string, int> _memoryLevels = new Dictionary<string, int>();
    /// <summary>Memories tab: one of the chosen principles is enough, not all of them.</summary>
    private bool _memoryAny;
    /// <summary>
    /// Memories tab mode: memories from objects examined in Consider instead of rereading books.
    /// A mode of its own, not mixed in: Consider is a single verb, one memory at a time, so it's
    /// the occasional way.
    /// </summary>
    private bool _memoriesFromItems;
    /// <summary>Memories tab mode: the well, a fireplace, the sea (renounce, swim).</summary>
    private bool _memoriesFromPlaces;
    /// <summary>Memories tab mode: memories crafted at a desk with a skill the player has.</summary>
    private bool _memoriesFromCrafts;
    /// <summary>Memories tab mode: the weathers (each day's weather is a memory card) and the forecast.</summary>
    private bool _memoriesFromWeather;

    private RectTransform _itemsPanel;
    private TextMeshProUGUI _principleCaption;
    private RectTransform _tagGrid;

    private RectTransform _desksPanel;
    /// <summary>The Souls tab's filters: pick one soul to see where and how it can be evolved.</summary>
    private RectTransform _soulsPanel;
    private readonly DeskFilter _soulFilter = new DeskFilter { Kind = PlaceKind.Soul };
    private RectTransform _soulGrid;
    private readonly List<Chip> _soulChips = new List<Chip>();
    private TextMeshProUGUI _deskCaption;

    private RectTransform _peoplePanel;
    private TextMeshProUGUI _peopleCaption;
    /// <summary>People tab, "Invite": visitors whose address is noted, with letter-writing steps.</summary>
    private bool _inviteMode;
    /// <summary>People tab: the Sweet Bones deck, fixed above the list (it doesn't scroll with it).</summary>
    private RectTransform _deckArea;
    /// <summary>People tab: the principles filter (caption + row), hidden in Invite mode to give the list room.</summary>
    private RectTransform _peoplePrinciples;

    private RectTransform _curesPanel;
    /// <summary>Cookbook tab: kitchen recipes the player has cooked before (CookBook).</summary>
    private RectTransform _cookPanel;
    /// <summary>Cookbook filter: 0 all, 1 food (sustenance), 2 drinks (beverage).</summary>
    private int _cookKind;
    /// <summary>Cookbook filter: the course a formal meal needs (course.first...), or null for any.</summary>
    private string _cookCourse;
    /// <summary>Courses of a formal meal (Lunch, Supper): the table needs one dish of each.</summary>
    private static readonly (string Aspect, string Label)[] Courses =
        { ("course.first", "First"), ("course.main", "Main"), ("course.side", "Side"), ("course.pudding", "Pudding") };
    /// <summary>Cookbook filter: the dish must have at least these principle levels.</summary>
    private readonly Dictionary<string, int> _cookLevels = new Dictionary<string, int>();
    private RectTransform _inksPanel;
    private RectTransform _cureGrid;
    private TextMeshProUGUI _cureGridCaption;
    /// <summary>Cures tab mode: souls with a malady instead of infected books.</summary>
    private bool _cureSouls;
    /// <summary>Contamination shown on the Cures tab, at most one (radio chips); empty means all.</summary>
    private readonly HashSet<string> _cureFilter = new HashSet<string>();
    /// <summary>Second, independent scroll area under the books: the cure for the picked book.</summary>
    private RectTransform _cureFrame;
    private RectTransform _cureContent;
    private ScrollRect _cureScroll;
    private ItemEntry _cureBook;
    private readonly Dictionary<ItemEntry, Image> _cureBookRows = new Dictionary<ItemEntry, Image>();

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
        // Places are mostly opened to find a desk: entering the tab starts on Desks.
        if (tab == Tab.Places && _tab != Tab.Places)
            _deskFilter.Kind = PlaceKind.Desk;
        _tab = tab;
        _booksPanel.gameObject.SetActive(tab == Tab.Books);
        _memoriesPanel.gameObject.SetActive(tab == Tab.Memories);
        _itemsPanel.gameObject.SetActive(tab == Tab.Items);
        _peoplePanel.gameObject.SetActive(tab == Tab.People);
        _desksPanel.gameObject.SetActive(tab == Tab.Places);
        _curesPanel.gameObject.SetActive(tab == Tab.Cures);
        _inksPanel.gameObject.SetActive(tab == Tab.Inks);
        _soulsPanel.gameObject.SetActive(tab == Tab.Souls);
        _cookPanel.gameObject.SetActive(tab == Tab.Cookbook);
        _cureFrame.gameObject.SetActive(tab == Tab.Cures);
        ((TextMeshProUGUI)_search.placeholder).text = tab == Tab.Books ? "Search books, records, films…"
            : tab == Tab.Memories ? "Search memories or books…"
            : tab == Tab.Items ? "Search items by name…"
            : tab == Tab.People ? "Search people by name…"
            : tab == Tab.Cures ? "Search infected books…"
            : tab == Tab.Inks ? "Search inks or skills…"
            : tab == Tab.Souls ? "Search places by name, room or Wisdom…"
            : tab == Tab.Cookbook ? "Search dishes or ingredients…"
            : "Search places by name, room or Wisdom…";
        // A fresh scan per tab switch: the world changes while the window stays open (a book
        // cured, a card used up). A tab switch is rare enough for a full scan.
        Rescan();
    }

    private float _nextCureCheck;

    /// <summary>
    /// On the Memories tab, every 2 s: did a book giving a memory move (put into a desk, a recipe
    /// started or finished)? Then rescan, so "Being made" follows. Compares each mastered book's
    /// sphere with the one seen at scan time: a cheap check, no world scan.
    /// </summary>
    private void CheckMemoryBooksMoved() => CheckCardsMoved(i => i.Book?.Memory != null);

    /// <summary>Every 2 s: rescan if any matching card left the sphere it was scanned in (or is gone).</summary>
    private void CheckCardsMoved(Func<ItemEntry, bool> watched)
    {
        if (Time.unscaledTime < _nextCureCheck)
            return;
        _nextCureCheck = Time.unscaledTime + 2f;
        foreach (ItemEntry item in _items)
        {
            if (!watched(item))
                continue;
            if (item.Token == null || !item.Token.IsValid() || item.Token.Sphere != item.Sphere)
            {
                Rescan();
                return;
            }
        }
    }

    /// <summary>
    /// On the Cures tab, every 2 s: has a listed book lost its contamination (cured) or gone?
    /// Then rescan, so it drops off the list without reopening the window. Only the few infected
    /// books are checked, not the whole world.
    /// </summary>
    private void Update()
    {
        if (_tab == Tab.Memories)
        {
            CheckMemoryBooksMoved();
            return;
        }
        if (_tab == Tab.Inks)
        {
            CheckCardsMoved(i => i.Aspects.AspectValue("skill") > 0 || i.Aspects.AspectValue("ability") > 0);
            return;
        }
        if (_tab != Tab.Cures || Time.unscaledTime < _nextCureCheck)
            return;
        _nextCureCheck = Time.unscaledTime + 2f;
        foreach (ItemEntry item in _items)
        {
            if (item.Book == null || item.Book.Contaminations.Count == 0)
                continue;
            bool stale = item.Stack == null || !item.Stack.IsValid() || item.Token == null || !item.Token.IsValid()
                || item.Book.Contaminations.Any(c => item.Stack.GetAspects(true).AspectValue(c) <= 0);
            if (stale)
            {
                Rescan();
                return;
            }
        }
    }

    // ---------------------------------------------------------------- layout

    private void Build()
    {
        _root.anchorMin = _root.anchorMax = new Vector2(0f, 0.5f);
        _root.pivot = new Vector2(0f, 0.5f);
        _root.anchoredPosition = new Vector2(40f, 0f);
        _root.sizeDelta = new Vector2(700f, 860f);
        GameStyle.Layout<VerticalLayoutGroup>(_root, 0f, null, TextAnchor.UpperCenter).childForceExpandWidth = true;
        GameStyle.Background(_root, "slots_bg_outline", Color.white);
        GameStyle.Background(_root, "slot_bg_grey", new Color(1f, 1f, 1f, 0.95f)).raycastTarget = true;

        BuildHeader();

        RectTransform body = GameStyle.Rect("Body", _root);
        GameStyle.Layout<VerticalLayoutGroup>(body, 8f, new RectOffset(22, 22, 10, 20), TextAnchor.UpperLeft).childForceExpandWidth = true;
        GameStyle.Size(body).flexibleHeight = 1f;

        RectTransform tabs = GameStyle.Rect("Tabs", body);
        GameStyle.Layout<HorizontalLayoutGroup>(tabs, 5f);
        GameStyle.Size(tabs, height: 30f);
        foreach (Tab tab in new[] { Tab.Books, Tab.Memories, Tab.Cures, Tab.Inks, Tab.Items, Tab.People, Tab.Places, Tab.Souls, Tab.Cookbook })
        {
            Tab captured = tab;
            // "Cookbook" doesn't fit the 68 px chip.
            string label = tab == Tab.Cookbook ? "Cook" : tab.ToString();
            Chip chip = TextChip(tabs, label, 68f, () => _tab == captured, () => SwitchTab(captured));
            chip.Label.fontSize = 14f;
            _chips.Add(chip);
        }

        _search = GameStyle.InputField(body, "Search books by title…");
        GameStyle.Size(_search, height: 42f);

        // A cross at the right end clears the query; shown only while there is text.
        _search.textViewport.offsetMax = new Vector2(-38f, _search.textViewport.offsetMax.y);
        Image clear = GameStyle.Panel(_search.transform, "Clear", "window_x", GameStyle.Gold);
        RectTransform clearRect = clear.rectTransform;
        clearRect.anchorMin = clearRect.anchorMax = new Vector2(1f, 0.5f);
        clearRect.pivot = new Vector2(1f, 0.5f);
        clearRect.sizeDelta = new Vector2(16f, 16f);
        clearRect.anchoredPosition = new Vector2(-14f, 0f);
        clear.preserveAspect = true;
        Button clearButton = clear.gameObject.AddComponent<Button>();
        ColorBlock clearColors = clearButton.colors;
        clearColors.highlightedColor = new Color(1.5f, 1.4f, 1.2f, 1f);
        clearButton.colors = clearColors;
        clearButton.onClick.AddListener(() =>
        {
            _search.text = "";
            _search.ActivateInputField();
        });
        clear.gameObject.SetActive(false);

        _search.onValueChanged.AddListener(text =>
        {
            _query = text.Trim();
            clear.gameObject.SetActive(text.Length > 0);
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
        BuildCuresPanel(body);
        _curesPanel.gameObject.SetActive(false);
        _soulsPanel = Panel(body, "SoulsFilters");
        Caption(_soulsPanel, "Soul to evolve");
        _soulGrid = Grid(_soulsPanel, "Souls", new Vector2(120f, 26f), 5);
        _soulsPanel.gameObject.SetActive(false);
        _inksPanel = Panel(body, "InksFilters");
        Caption(_inksPanel, "Click in order: desk, skill, soul - then start it in the desk");
        _inksPanel.gameObject.SetActive(false);
        _cookPanel = Panel(body, "CookFilters");
        Caption(_cookPanel, "Dishes and brews you have made · red: an ingredient you don't have");
        RectTransform kinds = Row(_cookPanel, "CookKinds", 28f);
        foreach ((int kind, string label) in new[] { (0, "All"), (1, "Food"), (2, "Drinks") })
            _chips.Add(TextChip(kinds, label, 90f, () => _cookKind == kind, () => _cookKind = kind));
        Caption(_cookPanel, "Course (formal meal: one of each)");
        RectTransform courses = Row(_cookPanel, "CookCourses", 28f);
        _chips.Add(TextChip(courses, "Any", 90f, () => _cookCourse == null, () => _cookCourse = null));
        foreach ((string aspect, string label) in Courses)
            _chips.Add(TextChip(courses, label, 90f, () => _cookCourse == aspect, () => _cookCourse = _cookCourse == aspect ? null : aspect));
        const string cookLevelText = "Dish with at least";
        TextMeshProUGUI cookCaption = Caption(_cookPanel, cookLevelText);
        RectTransform cookLevels = Row(_cookPanel, "CookLevels", 40f);
        foreach (string principle in WorldIndex.Principles)
            AddLevelChip(cookLevels, principle, _cookLevels, cookCaption, cookLevelText);
        _cookPanel.gameObject.SetActive(false);

        BuildResults(body);
    }

    private void BuildBooksPanel(Transform body)
    {
        _booksPanel = Panel(body, "BooksFilters");

        // Shown only while a visitor is in the house (see RebuildBookChips). The caption sits in the
        // row itself: one more line of filters would push the results out of the window.
        _guestRow = Row(_booksPanel, "Guests", 28f);
        _guestCaption = Caption(_guestRow, "For guest");
        GameStyle.Size(_guestCaption, 90f, 22f);

        // Shown only while a constellation window on the Tree screen asks for a text.
        _requestRow = Row(_booksPanel, "Requests", 28f);
        GameStyle.Size(Caption(_requestRow, "For constellation"), 130f, 22f);

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
        // In the Status row, not a row of its own: one more line would push the results down.
        GameStyle.Size(GameStyle.Rect("Gap", status), 24f, 24f);
        AddCheckbox(status, "Hand placement",
            "Double click a book: into the open window that takes it, else into a free slot of your things (5 slots)",
            () => BookFinderPlugin.BooksToHand != null && BookFinderPlugin.BooksToHand.Value,
            on => { if (BookFinderPlugin.BooksToHand != null) BookFinderPlugin.BooksToHand.Value = on; });

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
        GameStyle.Size(GameStyle.Rect("Gap", row), 8f, 24f);
        AddCheckbox(row, "Any", "Any one of the chosen principles is enough (off: all of them)",
            () => _memoryAny, on => { _memoryAny = on; Refresh(); });
        Caption(_memoriesPanel, "Get it by");
        RectTransform sources = Row(_memoriesPanel, "MemorySources", 28f);
        // One mode at a time: each chip clears the others.
        Action none = () => { _memoriesFromItems = false; _memoriesFromPlaces = false; _memoriesFromCrafts = false; _memoriesFromWeather = false; };
        _chips.Add(TextChip(sources, "Rereading books", 140f, () => !_memoriesFromItems && !_memoriesFromPlaces && !_memoriesFromCrafts && !_memoriesFromWeather,
            none));
        _chips.Add(TextChip(sources, "Consider an object", 150f, () => _memoriesFromItems, () => { none(); _memoriesFromItems = true; }));
        _chips.Add(TextChip(sources, "Well, fire, sea", 120f, () => _memoriesFromPlaces, () => { none(); _memoriesFromPlaces = true; }));
        _chips.Add(TextChip(sources, "Craft at a desk", 120f, () => _memoriesFromCrafts, () => { none(); _memoriesFromCrafts = true; }));
        _chips.Add(TextChip(sources, "Weather", 80f, () => _memoriesFromWeather, () => { none(); _memoriesFromWeather = true; }));
    }

    /// <summary>
    /// A small checkbox with a short label: a framed box that shows the game's own check mark sprite
    /// ("slot yes") when on. The longer explanation goes to the header on hover.
    /// </summary>
    private void AddCheckbox(Transform parent, string label, string tooltip, Func<bool> isOn, Action<bool> set)
    {
        RectTransform root = GameStyle.Rect("Checkbox", parent);
        GameStyle.Layout<HorizontalLayoutGroup>(root, 8f, null, TextAnchor.MiddleLeft);
        GameStyle.Size(root, height: 24f);
        Image box = GameStyle.Panel(root, "Box", "box-rnd-16px", new Color(0f, 0f, 0f, 0.55f));
        GameStyle.Size(box, 20f, 20f);
        Image mark = GameStyle.Panel(box.transform, "Mark", "slot yes", Color.white);
        mark.preserveAspect = true;
        mark.rectTransform.anchorMin = mark.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        mark.rectTransform.sizeDelta = new Vector2(18f, 18f);
        mark.raycastTarget = false;
        mark.gameObject.SetActive(isOn());
        TextMeshProUGUI text = GameStyle.Text(root, "Label", label, 15f, GameStyle.Cream, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        GameStyle.Size(text, height: 24f);
        Image hit = root.gameObject.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f); // catches clicks on the label and the gaps too
        Button button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = box;
        button.onClick.AddListener(() =>
        {
            set(!isOn());
            mark.gameObject.SetActive(isOn());
        });
        var hover = root.gameObject.AddComponent<HoverHandler>();
        string before = null;
        hover.Enter = () => { before = _count.text; _count.text = tooltip; };
        hover.Exit = () => { if (before != null) _count.text = before; };
    }

    private void BuildItemsPanel(Transform body)
    {
        _itemsPanel = Panel(body, "ItemsFilters");

        AddCheckbox(Row(_itemsPanel, "DoubleClick", 24f), "Hand placement",
            "Double click a card: into the open window that takes it, else into a free slot of your things (5 slots)",
            () => BookFinderPlugin.ItemsToHand != null && BookFinderPlugin.ItemsToHand.Value,
            on => { if (BookFinderPlugin.ItemsToHand != null) BookFinderPlugin.ItemsToHand.Value = on; });

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
        _peoplePrinciples = GameStyle.Rect("PrinciplesBlock", _peoplePanel);
        GameStyle.Layout<VerticalLayoutGroup>(_peoplePrinciples, 4f, null, TextAnchor.UpperLeft).childForceExpandWidth = true;
        _peopleCaption = Caption(_peoplePrinciples, "Principles");
        RectTransform principles = Row(_peoplePrinciples, "Principles", 40f);
        foreach (string principle in WorldIndex.Principles)
            AddPrincipleChip(principles, principle, _peopleFilter.Principles, _peopleCaption, "Principles");

        Caption(_peoplePanel, "Who");
        RectTransform who = Row(_peoplePanel, "Who", 28f);
        _chips.Add(TextChip(who, "All", 96f, () => _peopleFilter.Kind == null, () => _peopleFilter.Kind = null));
        _chips.Add(TextChip(who, "Visitors", 96f, () => _peopleFilter.Kind == PersonKind.Visitor, () => _peopleFilter.Kind = PersonKind.Visitor));
        _chips.Add(TextChip(who, "Assistants", 96f, () => _peopleFilter.Kind == PersonKind.Assistant, () => _peopleFilter.Kind = PersonKind.Assistant));
        GameStyle.Size(GameStyle.Rect("Gap", who), 24f);
        _chips.Add(TextChip(who, "All met", 96f, () => !_peopleFilter.NumaOnly, () => _peopleFilter.NumaOnly = false));
        _chips.Add(TextChip(who, "Numa", 96f, () => _peopleFilter.NumaOnly, () => _peopleFilter.NumaOnly = true));
        GameStyle.Size(GameStyle.Rect("Gap2", who), 24f);
        Chip invite = TextChip(who, "Invite", 80f, () => _inviteMode, () => _inviteMode = !_inviteMode, GameStyle.Sprite("slot yes"));
        invite.Label.fontStyle = FontStyles.SmallCaps | FontStyles.Bold;
        _chips.Add(invite);

        _deckArea = GameStyle.Rect("SweetBonesDeck", _peoplePanel);
        GameStyle.Layout<VerticalLayoutGroup>(_deckArea, 4f, null, TextAnchor.UpperLeft).childForceExpandWidth = true;
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
        // Soul places are still listed under All; evolving souls has its own tab (Souls).
        foreach (PlaceKind kind in new[] { PlaceKind.Desk, PlaceKind.Bed, PlaceKind.House, PlaceKind.Village, PlaceKind.Outdoors })
        {
            PlaceKind captured = kind;
            _chips.Add(TextChip(kinds, KindLabel(kind), 80f, () => _deskFilter.Kind == captured, () => _deskFilter.Kind = captured));
        }

        Caption(_desksPanel, "State");
        RectTransform state = Row(_desksPanel, "State", 28f);
        _chips.Add(TextChip(state, "Any state", 96f, () => !_deskFilter.IdleOnly, () => _deskFilter.IdleOnly = false));
        _chips.Add(TextChip(state, "Idle only", 96f, () => _deskFilter.IdleOnly, () => _deskFilter.IdleOnly = true));

    }

    private void BuildCuresPanel(Transform body)
    {
        _curesPanel = Panel(body, "CuresFilters");
        RectTransform mode = Row(_curesPanel, "CureMode", 28f);
        _chips.Add(TextChip(mode, "Books", 110f, () => !_cureSouls, () => _cureSouls = false));
        _chips.Add(TextChip(mode, "Souls", 110f, () => _cureSouls, () => _cureSouls = true));
        _cureGridCaption = Caption(_curesPanel, "Contamination");
        // One chip per contamination some book carries now (see RebuildBookChips).
        _cureGrid = Grid(_curesPanel, "CureContaminations", new Vector2(141f, 26f), 4);
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
        (RectTransform frame, RectTransform content, ScrollRect scroll) = BuildScrollArea(body, "Results");
        GameStyle.Size(frame).flexibleHeight = 1f;
        GameStyle.Size(frame).minHeight = 120f; // low: the Books filters are tall, the list gives way first
        _results = content;
        _scroll = scroll;

        // Cures tab only: the cure for the picked book, scrolled on its own.
        (_cureFrame, _cureContent, _cureScroll) = BuildScrollArea(body, "CureDetails");
        GameStyle.Size(_cureFrame).preferredHeight = 330f;
        GameStyle.Size(_cureFrame).flexibleHeight = 0f;
        _cureFrame.gameObject.SetActive(false);
    }

    private static (RectTransform frame, RectTransform content, ScrollRect scroll) BuildScrollArea(Transform body, string name)
    {
        RectTransform frame = GameStyle.Rect(name, body);
        GameStyle.Background(frame, "slots_bg", new Color(1f, 1f, 1f, 0.8f));

        RectTransform viewport = GameStyle.Rect("Viewport", frame);
        GameStyle.Stretch(viewport, 6, 6, 6, 6);
        viewport.gameObject.AddComponent<RectMask2D>();
        GameStyle.Size(viewport).ignoreLayout = true;

        RectTransform content = GameStyle.Rect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        GameStyle.Layout<VerticalLayoutGroup>(content, 3f, null, TextAnchor.UpperLeft).childForceExpandWidth = true;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = frame.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        AddScrollbar(scroll, viewport, 6f);
        WheelStepper.Attach(scroll, 57f); // result row 54 + spacing 3
        return (frame, content, scroll);
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

        // Named like the result rows' aspect icons: a middle click pins the aspect (AspectPin).
        Image icon = GameStyle.Panel(rect, "Aspect_" + principle, "", Color.white);
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
        AddRightClickOff(rect.gameObject, () => selection.Contains(principle), () => selection.Remove(principle));

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
        // Named like the result rows' aspect icons: a middle click pins the aspect (AspectPin).
        Image icon = GameStyle.Panel(rect, "Aspect_" + principle, "", Color.white);
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
            if (button == PointerEventData.InputButton.Middle)
                return; // middle click pins the aspect instead (AspectPin)
            levels.TryGetValue(principle, out int level);
            level = Mathf.Clamp(level + (button == PointerEventData.InputButton.Right ? -1 : 1), 0, 20);
            if (level == 0)
                levels.Remove(principle);
            else
                levels[principle] = level;
            Refresh();
        };
        var hover = rect.gameObject.AddComponent<HoverHandler>();
        hover.Enter = () => caption.text = label + ": left click +1, right click -1, middle click pins";
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
        // Toggle chips turn off; radio chips (a pick, or "All"/"Any") clear or stay as they are.
        AddRightClickOff(bg.gameObject, isOn, toggle);
        return new Chip { Root = bg.gameObject, Background = bg, Outline = outline, Label = label, Icon = iconImage, IsOn = isOn };
    }

    /// <summary>
    /// Right click turns a lit chip off, like right click lowers a level chip. (Button reacts to
    /// the left button only, so it doesn't fire too.) Unlit chips ignore it.
    /// </summary>
    private void AddRightClickOff(GameObject chip, Func<bool> isOn, Action turnOff)
    {
        var clicks = chip.AddComponent<ClickHandler>();
        clicks.Click = button =>
        {
            if (button != PointerEventData.InputButton.Right || !isOn())
                return;
            turnOff();
            Refresh();
        };
    }

    /// <summary>Radio pick: only this value; clicking the picked one clears the set.</summary>
    private static void PickOnly<T>(HashSet<T> set, T value)
    {
        bool wasPicked = set.Contains(value);
        set.Clear();
        if (!wasPicked)
            set.Add(value);
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

        // Visitors who can take a book now: in the house, not the departing ("embarking_") card.
        List<PersonEntry> guests = _people
            .Where(p => p.Kind == PersonKind.Visitor && p.Here && p.Stack != null
                        && !p.Stack.EntityId.StartsWith("embarking_", StringComparison.Ordinal))
            .ToList();
        if (_bookFilter.Guest != null)
            _bookFilter.Guest = guests.FirstOrDefault(g => g.PersonId == _bookFilter.Guest.PersonId); // fresh aspects, or gone
        _guestRow.gameObject.SetActive(guests.Count > 0);
        foreach (PersonEntry guest in guests)
        {
            PersonEntry captured = guest;
            _bookChips.Add(TextChip(_guestRow, guest.Name, -1f,
                () => _bookFilter.Guest?.PersonId == captured.PersonId,
                () => PickGuest(captured),
                SpriteOf(guest.Element)));
        }

        List<NxRequest> requests = NxRequests.ForOpenWindow();
        if (_bookFilter.Request != null)
            _bookFilter.Request = requests.FirstOrDefault(r => r.Label == _bookFilter.Request.Label); // or the step is done
        _requestRow.gameObject.SetActive(requests.Count > 0);
        foreach (NxRequest request in requests)
        {
            NxRequest captured = request;
            _bookChips.Add(TextChip(_requestRow, request.Label, -1f,
                () => _bookFilter.Request?.Label == captured.Label,
                () => PickRequest(captured),
                SpriteOf(request.Visitor)));
        }

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

        _cureFilter.IntersectWith(contaminations); // drop cured ones, or they'd hide everything
        foreach (string contamination in contaminations)
        {
            string captured = contamination;
            _bookChips.Add(TextChip(_cureGrid, WorldIndex.LabelOf(contamination), -1f,
                () => _cureFilter.Contains(captured),
                () => PickOnly(_cureFilter, captured),
                SpriteOf(contamination)));
        }

        List<string> languages = books.Select(b => b.Language ?? WorldIndex.NoLanguage).Distinct()
            .OrderBy(id => id == WorldIndex.NoLanguage ? "" : WorldIndex.LanguageName(id)).ToList();
        // "Known": every language the player can read now, picked in one click (again to clear).
        HashSet<string> known = new HashSet<string>(languages.Where(IsKnownLanguage));
        Chip knownChip = TextChip(_languageGrid, "Known", -1f,
            () => known.Count > 0 && _bookFilter.Languages.SetEquals(known),
            () =>
            {
                bool wasKnown = _bookFilter.Languages.SetEquals(known);
                _bookFilter.Languages.Clear();
                if (!wasKnown)
                    _bookFilter.Languages.UnionWith(known);
            },
            GameStyle.Sprite("slot yes"));
        // It picks a group, not one language: set apart by the game's check mark, italics and a
        // warm tint under the label (a child image, so Chip.Update's colours don't touch it).
        knownChip.Label.fontStyle = FontStyles.SmallCaps | FontStyles.Italic | FontStyles.Bold;
        Image tint = GameStyle.Panel(knownChip.Root.transform, "Tint", "slots_bg", new Color(0.85f, 0.6f, 0.25f, 0.35f));
        GameStyle.Stretch(tint.rectTransform, 2, 2, 2, 2);
        tint.raycastTarget = false;
        tint.transform.SetAsFirstSibling();
        _bookChips.Add(knownChip);
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
    /// Readable now: native text; the classical languages the Librarian reads from the start
    /// (Latin, Greek, Aramaic, Phrygian, Sanskrit have no skill, so no element s.&lt;language&gt;);
    /// or a language whose skill card (s.vak for w.vak) the player has.
    /// </summary>
    private bool IsKnownLanguage(string language)
    {
        if (language == WorldIndex.NoLanguage)
            return true;
        string skill = "s." + language.Substring("w.".Length);
        // The compendium answers unknown ids with a NullElement (id NULL_ELEMENT_ID), not null.
        Element skillElement = WorldIndex.ElementOf(skill);
        if (skillElement == null || skillElement.Id != skill)
            return true;
        return _items.Any(i => i.Stack != null && i.Stack.EntityId == skill);
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
            // Easiest first; books without a mystery (no difficulty) last; books a desk is
            // working with (being read, used in a craft) after all the free ones.
            matches = _items.Where(i => _bookFilter.Matches(i, _query))
                .OrderBy(i => BusyHost(i) != null)
                .ThenBy(i => i.Book.Mystery != null ? i.Book.Level : int.MaxValue)
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
        else if (_tab == Tab.Souls)
        {
            RebuildSoulChips();
            List<DeskEntry> soulPlaces = _desks.Where(d => _soulFilter.Matches(d, _query))
                .OrderBy(d => d.IsIdle ? 0 : d.IsDone ? 1 : 2)
                .ToList();
            foreach (DeskEntry desk in soulPlaces.Take(MaxRows))
                AddDeskRow(desk);
            if (soulPlaces.Count == 0 && _soulFilter.Souls.Count == 1)
                AddNoEvolvePlaceRow(_soulFilter.Souls.First());
            _count.text = $"{soulPlaces.Count} of {_desks.Count(d => d.Kind == PlaceKind.Soul)}";
            _scroll.verticalNormalizedPosition = 1f;
            return;
        }
        else if (_tab == Tab.Places)
        {
            total = _desks.Count;
            // Free places first, then finished ones (to collect), busy ones last.
            List<DeskEntry> desks = _desks.Where(d => _deskFilter.Matches(d, _query))
                .OrderBy(d => d.IsIdle ? 0 : d.IsDone ? 1 : 2)
                .ToList();
            foreach (DeskEntry desk in desks.Take(MaxRows))
                AddDeskRow(desk);
            _count.text = $"{desks.Count} of {total}";
            _scroll.verticalNormalizedPosition = 1f;
            return;
        }
        else if (_tab == Tab.Cookbook)
        {
            RefreshCookbook();
            _scroll.verticalNormalizedPosition = 1f;
            return;
        }
        else if (_tab == Tab.Inks)
        {
            RefreshInks();
            _scroll.verticalNormalizedPosition = 1f;
            return;
        }
        else if (_tab == Tab.Cures)
        {
            _cureGridCaption.gameObject.SetActive(!_cureSouls);
            _cureGrid.gameObject.SetActive(!_cureSouls);
            GameStyle.Size(_cureFrame).preferredHeight = 330f; // Invite shrinks it; the cure needs the room
            _cureFrame.gameObject.SetActive(!_cureSouls);
            if (_cureSouls)
                RefreshCureSouls();
            else
                RefreshCures();
            _scroll.verticalNormalizedPosition = 1f;
            return;
        }
        else if (_tab == Tab.People && _inviteMode)
        {
            _cureFrame.gameObject.SetActive(true); // the second area: the picked guest's recipe
            // Invite needs room for the list: no principles filter, and the recipe area only as tall
            // as a recipe (header, desks, cards, total) instead of the Cures tab's 330.
            _peoplePrinciples.gameObject.SetActive(false);
            GameStyle.Size(_cureFrame).preferredHeight = 205f;
            AddSweetBonesDeck(); // hides itself in Invite mode
            RefreshInvites();
            _scroll.verticalNormalizedPosition = 1f;
            return;
        }
        else if (_tab == Tab.People)
        {
            _cureFrame.gameObject.SetActive(false);
            _peoplePrinciples.gameObject.SetActive(true);
            total = _people.Count;
            AddSweetBonesDeck();
            List<PersonEntry> people = _people.Where(p => _peopleFilter.Matches(p, _query)).ToList();
            foreach (PersonEntry person in people.Take(MaxRows))
                AddPersonRow(person);
            _count.text = $"{people.Count} of {total} met";
            _scroll.verticalNormalizedPosition = 1f;
            return;
        }
        else
        {
            // Readables (books, records, films) have their own tab; souls, skills and lessons
            // aren't things at all.
            List<ItemEntry> things = _items.Where(i => i.Book == null && !NotItems.Any(a => i.Aspects.AspectValue(a) > 0)).ToList();
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
            () => FocusItem(item), onDoubleClick: () => SendFromRow(item));
    }

    /// <summary>
    /// A double click on an item row: into the open window that takes it (desk, Consider...), as
    /// always. On the Items tab with "to my things" ticked, a card no open window takes goes into a
    /// free slot of the carried things instead (the game's own SendToPortage: open situation
    /// first, then ~/portage1 and its linked slots).
    /// </summary>
    private void SendFromRow(ItemEntry item)
    {
        BepInEx.Configuration.ConfigEntry<bool> toHand = _tab == Tab.Items ? BookFinderPlugin.ItemsToHand
            : _tab == Tab.Books ? BookFinderPlugin.BooksToHand : null;
        if (toHand == null || !toHand.Value)
        {
            SendToOpenWindow(item);
            return;
        }
        Token token = item.Token;
        if (token == null || !token.IsValid())
            return;
        if (SecretHistories.Logic.TokenDestinationSender.SendToOpenSituation(token))
        {
            _count.text = $"Sent: {item.Title}";
            return;
        }
        bool carried = SecretHistories.Logic.BoHTokenSender.SendToPortage(token);
        _count.text = carried ? $"To your things: {item.Title}" : "No open window takes this, and your things are full";
    }

    /// <summary>
    /// Double click: into a fitting slot of the open desk or Consider window, like double-clicking
    /// the card itself - but only that half of the game's behaviour: Token.SendToOpenSituation
    /// does nothing when no open window takes the card, where the card's own double click
    /// (BoHTokenSender.SendToPortage) would fall back to the hand.
    /// </summary>
    private void SendToOpenWindow(ItemEntry item) => SendToOpenWindow(item.Token, item.Title);

    /// <summary>
    /// Double click on a place: fly there and open its window, as clicking it on the table would
    /// (Situation.OpenAt). The Finder stays open, so books can then be double-clicked into it.
    /// </summary>
    private void OpenPlace(DeskEntry desk)
    {
        Focus(desk.Token, desk.Situation, desk.Name);
        if (desk.Situation != null && desk.Token != null && desk.Token.IsValid())
            desk.Situation.OpenAt(desk.Token.Location);
    }

    /// <summary>Also for people: a visitor or assistant who is here can go into a Talk or salon slot.</summary>
    private void SendToOpenWindow(Token token, string title)
    {
        bool sent = token != null && token.IsValid() && SecretHistories.Logic.TokenDestinationSender.SendToOpenSituation(token);
        _count.text = sent ? $"Sent: {title}" : "No open window takes this";
    }

    /// <summary>Items inside a desk sit in its hidden window: fly to the desk, show the item.</summary>
    private void FocusItem(ItemEntry item)
    {
        if (item.HostToken != null && item.HostToken.IsValid())
            Focus(item.HostToken, item.Stack, item.Title);
        else
            Focus(item.Token, item.Stack, item.Title);
    }

    private Transform AddRow(Sprite artSprite, string title, string subtitle, Action onClick, int indent = 0,
        string markSprite = null, Color markColor = default, Action onDoubleClick = null, Transform parent = null)
    {
        Image row = GameStyle.Panel(parent ?? _results, "Row", "slots_bg", new Color(1f, 1f, 1f, indent > 0 ? 0.25f : 0.45f));
        GameStyle.Size(row, height: 54f);
        GameStyle.Layout<HorizontalLayoutGroup>(row, 10f, new RectOffset(8 + indent, 12, 4, 4));
        Button button = row.gameObject.AddComponent<Button>();
        button.targetGraphic = row;
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.6f, 1.4f, 1f, 1f);
        colors.pressedColor = new Color(2f, 1.7f, 1.1f, 1f);
        button.colors = colors;
        if (onDoubleClick == null)
            button.onClick.AddListener(() => onClick());
        else
        {
            // The single click waits a moment, so a double click doesn't fly the camera off first.
            var clicks = row.gameObject.AddComponent<SingleOrDoubleClick>();
            clicks.Single = onClick;
            clicks.Double = onDoubleClick;
        }

        Image art = GameStyle.Panel(row.transform, "Art", "", Color.white);
        art.sprite = artSprite;
        art.preserveAspect = true;
        art.raycastTarget = false;
        GameStyle.Size(art, 40f, 46f);

        RectTransform texts = GameStyle.Rect("Texts", row.transform);
        GameStyle.Layout<VerticalLayoutGroup>(texts, 0f, null, TextAnchor.MiddleLeft).childForceExpandWidth = true;
        GameStyle.Size(texts, flexibleWidth: 1f);
        GameStyle.Text(texts, "Title", title, 18f, GameStyle.Cream);
        if (markSprite == null)
        {
            GameStyle.Text(texts, "Where", subtitle, 14f, GameStyle.GoldFaint, FontStyles.Italic);
            return row.transform;
        }
        // A game icon (check mark, cross) leading the subtitle line.
        RectTransform line = GameStyle.Rect("WhereLine", texts);
        GameStyle.Layout<HorizontalLayoutGroup>(line, 4f, null, TextAnchor.MiddleLeft).childForceExpandWidth = false;
        Image mark = GameStyle.Panel(line, "Mark", markSprite, markColor);
        mark.preserveAspect = true;
        mark.raycastTarget = false;
        GameStyle.Size(mark, 14f, 14f);
        TextMeshProUGUI where = GameStyle.Text(line, "Where", subtitle, 14f, GameStyle.GoldFaint, FontStyles.Italic);
        GameStyle.Size(where, flexibleWidth: 1f);
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
        if (book.Subject != null)
            where = $"On {WorldIndex.SubjectName(book.Subject)} [{book.Soph}]" + (where.Length > 0 ? " · " + where : "");
        bool busy = BusyHost(item) != null;
        if (busy)
            where = "<color=#DDA8FF>Busy</color> · " + where;
        Transform row = AddRow(item, where);
        if (busy)
            Fade(row);

        // Fixed-width columns, so contaminations, mysteries and languages line up row under row.
        Transform infected = Column(row, "Contaminations", 58f, TextAnchor.MiddleRight);
        foreach (string contamination in book.Contaminations)
        {
            AspectIcon(infected, contamination, 24f);
        }
        Transform mystery = Column(row, "Mystery", 62f, TextAnchor.MiddleLeft);
        if (book.Mystery != null)
            AspectBadge(mystery, book.Mystery, book.Level, 28f, 20f);
        Transform language = Column(row, "Language", 28f, TextAnchor.MiddleCenter);
        if (book.Language != null)
            AddLanguageIcon(language, book.Language);

        // Mastered: the game's own check mark ("slot yes"); keeps room for long titles.
        Image mastered = GameStyle.Panel(row, "Mastered", "slot yes", Color.white);
        mastered.preserveAspect = true;
        mastered.raycastTarget = false;
        mastered.enabled = book.Mastered;
        GameStyle.Size(mastered, 22f, 22f);
    }

    /// <summary>
    /// Picks a guest (again to clear). Picking resets the filters that would fight the guest's own
    /// rules: Status to All (the guest doesn't care what the player has mastered), no Language chip
    /// (the guest's languages decide), and Contamination to Clean.
    /// </summary>
    /// <summary>
    /// Picks a constellation request (again to clear). Mystery and Difficulty chips would fight it
    /// (the [N] is the text's sophistication, not its mystery), so they're cleared, and the guest too.
    /// </summary>
    private void PickRequest(NxRequest request)
    {
        if (_bookFilter.Request?.Label == request.Label)
        {
            _bookFilter.Request = null;
            return;
        }
        _bookFilter.Request = request;
        _bookFilter.Guest = null;
        _bookFilter.Mysteries.Clear();
        _bookFilter.Levels.Clear();
    }

    private void PickGuest(PersonEntry guest)
    {
        if (_bookFilter.Guest?.PersonId == guest.PersonId)
        {
            _bookFilter.Guest = null;
            return;
        }
        _bookFilter.Guest = guest;
        _bookFilter.Request = null;
        _bookFilter.ReadState = ReadState.Any;
        _bookFilter.Languages.Clear();
        _bookFilter.Contaminations.Clear();
        if (_contaminationGrid.gameObject.activeSelf) // shown only while some book is infected
            _bookFilter.Contaminations.Add(BookFilter.NoContamination);
    }

    /// <summary>A fixed-width cell in a result row; its icons sit side by side inside it.</summary>
    private static Transform Column(Transform row, string name, float width, TextAnchor align)
    {
        RectTransform cell = GameStyle.Rect(name, row);
        GameStyle.Layout<HorizontalLayoutGroup>(cell, 4f, null, align);
        GameStyle.Size(cell, width, 46f);
        return cell;
    }

    /// <summary>
    /// The book's language: a click puts the language skill card (s.&lt;language&gt;) into the open
    /// window, like clicking a book. Latin, Greek, Aramaic, Phrygian and Sanskrit have no skill card
    /// (the Librarian reads them from the start), so there is nothing to send.
    /// </summary>
    private void AddLanguageIcon(Transform row, string language)
    {
        Image icon = AspectIcon(row, language, 26f);
        string name = WorldIndex.LanguageName(language);
        string skillId = "s." + language.Substring("w.".Length);
        ItemEntry skill = _items.FirstOrDefault(i => i.Stack?.EntityId == skillId && i.Token != null && i.Token.IsValid() && !SlotCheck.InRunningRecipe(i));
        string tip = skill != null ? $"{name}: click to put {skill.Title} into the open window" : $"{name}: no skill card to add";
        var hover = icon.gameObject.AddComponent<HoverHandler>();
        string before = null;
        hover.Enter = () => { before = _count.text; _count.text = tip; };
        hover.Exit = () => { if (before != null) _count.text = before; };
        if (skill == null)
            return;
        Button button = icon.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.4f, 1.3f, 1f, 1f);
        colors.pressedColor = new Color(1.8f, 1.6f, 1.1f, 1f);
        button.colors = colors;
        button.onClick.AddListener(() => SendOrFocus(skill));
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
    /// Memories are unique (every mem.* has "unique": true): when a second copy appears the game
    /// retires the first (HornedAxe.EnforceUniquenessIfStackIsUnique), so making one that is
    /// already on its way or already held is wasted. Those go into a separate section at the end,
    /// struck through, with an amber note (being made: where and how long) or a green one (held).
    /// </summary>
    /// <summary>The chosen principle levels: all of them, or with "Any" ticked one is enough.</summary>
    private bool MemoryLevelsMatch(Element element)
    {
        if (_memoryLevels.Count == 0)
            return true;
        Func<KeyValuePair<string, int>, bool> meets = r => (element?.Aspects?.AspectValue(r.Key) ?? 0) >= r.Value;
        return _memoryAny ? _memoryLevels.Any(meets) : _memoryLevels.All(meets);
    }

    private void RefreshMemories()
    {
        if (_memoriesFromPlaces)
        {
            RefreshPlaceMemories();
            return;
        }
        if (_memoriesFromCrafts)
        {
            RefreshCraftMemories();
            return;
        }
        if (_memoriesFromWeather)
        {
            RefreshWeatherMemories();
            return;
        }
        Dictionary<string, List<ItemEntry>> bookSources = _items.Where(i => !_memoriesFromItems && i.Book?.Memory != null)
            .GroupBy(i => i.Book.Memory)
            .ToDictionary(g => g.Key, g => g.OrderBy(i => i.Title).ToList());
        Dictionary<string, List<ItemEntry>> itemSources = _memoriesFromItems ? MemoryObjects() : new Dictionary<string, List<ItemEntry>>();
        var byMemory = bookSources.Keys.Union(itemSources.Keys)
            .Select(id => (memory: id, element: WorldIndex.ElementOf(id),
                books: bookSources.TryGetValue(id, out var b) ? b : new List<ItemEntry>(),
                objects: itemSources.TryGetValue(id, out var o) ? o : new List<ItemEntry>()))
            .Where(m => m.element != null)
            .ToList();
        int total = byMemory.Count;

        var shown = byMemory
            .Where(m => MemoryLevelsMatch(m.element))
            .Where(m => string.IsNullOrEmpty(_query)
                || WorldIndex.LabelOf(m.memory).IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0
                || m.books.Any(b => WorldIndex.TitleMatches(b, _query))
                || m.objects.Any(o => WorldIndex.TitleMatches(o, _query)))
            .OrderByDescending(m => _memoryLevels.Keys.Sum(p => m.element.Aspects?.AspectValue(p) ?? 0))
            .ThenBy(m => WorldIndex.LabelOf(m.memory), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var free = shown.Where(m => MemoryNote(m.memory, m.books) == null).ToList();
        var taken = shown.Where(m => MemoryNote(m.memory, m.books) != null).ToList();

        int rows = 0;
        foreach (var m in free)
        {
            if (rows >= MaxRows)
                break;
            rows += AddMemoryRows(m.memory, m.element, m.books, m.objects, null);
        }
        if (taken.Count > 0 && rows < MaxRows)
        {
            AddSectionLabel($"Already on the way or held ({taken.Count}) · another copy would only replace it", 0);
            foreach (var m in taken)
            {
                if (rows >= MaxRows)
                    break;
                rows += AddMemoryRows(m.memory, m.element, m.books, m.objects, MemoryNote(m.memory, m.books));
            }
        }
        _count.text = $"{free.Count} to make · {shown.Count} of {total}";
    }

    /// <summary>
    /// Why this memory shouldn't be made now, or null: a book of it is being read in a working
    /// desk ("Being made at Desk: Eva"), or a card of it already exists anywhere the
    /// player can reach - hand, table, an idle desk's slot, uncollected results ("You have it ·
    /// Hand"). One already used in a running recipe is spent and doesn't count.
    /// </summary>
    private string MemoryNote(string memory, List<ItemEntry> books)
    {
        ItemEntry busyBook = books.FirstOrDefault(b => BusyHost(b) != null);
        if (busyBook != null)
            return $"<color=#DDA8FF><b>Being made {BusyText(busyBook)}</b></color>";
        // A copy inside a working recipe doesn't count: slots and situation storage don't enforce
        // uniqueness (Sphere.EnforceUniqueStacksInThisSphere is false there, so the card is
        // deregistered), and the recipe's "fatiguing" turns the memory into an Echo at the end.
        // So it's spent, and a new copy can be made meanwhile.
        ItemEntry held = _items.FirstOrDefault(i => i.Stack != null && i.Stack.EntityId == memory
            && i.Token != null && i.Token.IsValid() && BusyHost(i) == null);
        if (held != null)
            return $"<color=#9CCB7A>You have it{(held.Location.Length > 0 ? " · " + held.Location : "")}</color>";
        return null;
    }

    /// <summary>
    /// Aspects whose examine recipe in Consider uses the object up ("Dissect", "Consume", "Drink",
    /// "Analyse"...: 2b_consider_generic, each linked to examine.considerable.consume). Beasts
    /// ("Inspect") and everything else ("Investigate") stay.
    /// </summary>
    private static readonly string[] ConsumedInConsider =
        { "beverage", "candle", "flower", "fruit", "leaf", "liquid", "mark", "material", "remains", "root", "sustenance" };

    private static bool UsedUpInConsider(ItemEntry item) =>
        ConsumedInConsider.Any(a => item.Aspects.AspectValue(a) > 0)
        || (item.Aspects.AspectValue("device") > 0 && item.Aspects.AspectValue("inert") <= 0);

    /// <summary>
    /// Memory -> reachable objects that give it when examined in Consider: "considerable" cards
    /// whose "scrutiny" xtrigger spawns a mem.* card. Those that stay first.
    /// </summary>
    private Dictionary<string, List<ItemEntry>> MemoryObjects()
    {
        var result = new Dictionary<string, List<ItemEntry>>();
        foreach (ItemEntry item in _items)
        {
            if (item.Book != null || item.Stack?.Element?.XTriggers == null || item.Aspects.AspectValue("considerable") <= 0
                || SlotCheck.InRunningRecipe(item) || !item.Stack.Element.XTriggers.TryGetValue("scrutiny", out List<MorphDetails> morphs))
                continue;
            foreach (MorphDetails morph in morphs)
            {
                if (morph.Id == null || !IsMemoryCard(morph.Id) || morph.MorphEffect.ToString() != "Spawn")
                    continue;
                if (!result.TryGetValue(morph.Id, out List<ItemEntry> list))
                    result[morph.Id] = list = new List<ItemEntry>();
                if (!list.Any(i => i.Stack.EntityId == item.Stack.EntityId)) // one of each kind
                    list.Add(item);
            }
        }
        foreach (List<ItemEntry> list in result.Values)
            list.Sort((a, b) => UsedUpInConsider(a) != UsedUpInConsider(b) ? (UsedUpInConsider(a) ? 1 : -1) : string.CompareOrdinal(a.Title, b.Title));
        return result;
    }

    /// <summary>
    /// A memory made at a place rather than from a book or an object (renounce.json,
    /// other_activities.json): drop a thing in the well (Memory: Sound) or a penny (Satisfaction),
    /// burn a thing in a fireplace (Sight), swim in the sea with a soul of Heart and Scale (Salt;
    /// with a basket of towels also Solace).
    /// </summary>
    private sealed class PlaceRoute
    {
        public string Memory;
        public string Also;
        public string How;
        public Func<Situation, bool> Verb;
        public Func<ItemEntry, bool> Item;
        public string ItemLabel;
        public bool NeedsSoul;
        public bool Towels;
    }

    private static readonly PlaceRoute[] PlaceRoutes =
    {
        new PlaceRoute { Memory = "mem.satisfaction", How = "Well · Renounce a penny", ItemLabel = "Penny",
            Verb = s => s.VerbId == "library.well", Item = i => i.Aspects.AspectValue("penny") > 0 },
        new PlaceRoute { Memory = "mem.sound", How = "Well · Renounce a thing (it's lost)", ItemLabel = "Spare",
            Verb = s => s.VerbId == "library.well", Item = i => IsSpare(i) },
        new PlaceRoute { Memory = "mem.sight", How = "Fireplace · Renounce a thing (it's lost)", ItemLabel = "Spare",
            Verb = s => s.VerbId.StartsWith("library.fireplace.", StringComparison.Ordinal),
            Item = i => IsSpare(i) && i.Aspects.AspectValue("brewable") <= 0 },
        new PlaceRoute { Memory = "mem.salt", Also = "mem.solace", How = "Sea · A Brisk Swim (a soul of Heart and Scale; towels add Solace)",
            Verb = s => s.VerbId.StartsWith("world.sea.", StringComparison.Ordinal), NeedsSoul = true, Towels = true },
    };

    /// <summary>
    /// A thing the player can spare: one of three or more cards of the same kind, and not a book,
    /// skill, soul, memory, journal, beast, furniture, wall art, food, drink or ink.
    /// </summary>
    private static readonly string[] NotSpare =
        { "readable", "skill", "ability", "memory", "journal", "beast", "comfort", "wallart", "sustenance", "beverage", "ink", "penny", "tool" };
    /// <summary>Cards left out of the Items tab: souls (with their maladies), skills and lessons.</summary>
    private static readonly string[] NotItems = { "ability", "skill", "lesson" };
    private static Dictionary<string, int> _spareCounts = new Dictionary<string, int>();

    private static bool IsSpare(ItemEntry i) =>
        i.Stack != null && i.Aspects.AspectValue("thing") > 0 && !NotSpare.Any(a => i.Aspects.AspectValue(a) > 0)
        && _spareCounts.TryGetValue(i.Stack.EntityId, out int n) && n >= 3;

    private void RefreshPlaceMemories()
    {
        _spareCounts = _items.Where(i => i.Stack != null && i.Book == null)
            .GroupBy(i => i.Stack.EntityId).ToDictionary(g => g.Key, g => g.Sum(i => Math.Max(1, i.Stack.Quantity)));
        List<Situation> verbs = Watchman.Get<HornedAxe>().GetRegisteredSituations()
            .Where(s => s?.VerbId != null && s.Token != null && s.Token.IsValid() && s.StateIdentifier == SecretHistories.Enums.StateEnum.Unstarted
                        && Reachable(s.Token))
            .ToList();
        int shown = 0;
        foreach (PlaceRoute route in PlaceRoutes)
        {
            Element element = WorldIndex.ElementOf(route.Memory);
            if (element == null || !MemoryLevelsMatch(element))
                continue;
            string label = WorldIndex.LabelOf(route.Memory) + (route.Also != null ? " + " + WorldIndex.LabelOf(route.Also) : "");
            string note = MemoryNote(route.Memory, new List<ItemEntry>());
            Transform row = AddRow(SpriteOf(element), note != null ? $"<s>{label}</s>" : label, note ?? route.How,
                () => Watchman.Get<AureateNotifier>().ShowAspectDetails(element, SpriteOf(element)));
            if (note != null)
                Tint(row);
            if (row.Find("Art")?.GetComponent<Image>() is Image art)
            {
                art.raycastTarget = true;
                string memory = route.Memory;
                art.gameObject.AddComponent<MiddleClick>().Click = () => GiveMemory(memory, WorldIndex.LabelOf(memory));
            }
            foreach (KeyValuePair<string, int> aspect in element.Aspects.Where(a => a.Value > 0 && WorldIndex.IsPrinciple(a.Key)).Take(5))
                AspectBadge(row, aspect.Key, aspect.Value, 22f, 16f);
            shown++;
            if (note != null)
                continue;
            List<Situation> places = verbs.Where(route.Verb).Take(16).ToList();
            AddStepStrip(new List<StripGroup> { new StripGroup("Place", places.Select(s => Step(SafeArt(s), s.Label, () =>
            {
                Focus(s.Token, s, s.Label);
                s.OpenAt(s.Token.Location);
            }))) }, 40);
            if (route.Item != null)
            {
                List<ItemEntry> things = _items.Where(i => i.Token != null && i.Token.IsValid() && !SlotCheck.InRunningRecipe(i) && route.Item(i))
                    .GroupBy(i => i.Stack.EntityId).Select(g => g.First())
                    .OrderBy(i => i.Aspects.Where(a => WorldIndex.IsPrinciple(a.Key)).Sum(a => a.Value)) // plainest first
                    .Take(16).ToList();
                AddStepStrip(new List<StripGroup> { new StripGroup(route.ItemLabel, things.Select(i => Step(SpriteOf(i.Stack.Element),
                    $"{i.Title}" + (_spareCounts.TryGetValue(i.Stack.EntityId, out int n) && n > 1 ? $" · you have {n}" : "") + (i.Location.Length > 0 ? " · " + i.Location : ""),
                    () => SendOrFocus(i)))) }, 40);
            }
            if (route.NeedsSoul)
            {
                List<ItemEntry> souls = _items.Where(s => s.Aspects.AspectValue("ability") > 0 && s.Aspects.AspectValue("fatigued") <= 0
                        && s.Aspects.AspectValue("heart") > 0 && s.Aspects.AspectValue("scale") > 0 && !SlotCheck.InRunningRecipe(s))
                    .GroupBy(s => s.Stack.EntityId).Select(g => g.First())
                    .OrderBy(s => s.Aspects.AspectValue("rank")).Take(6).ToList();
                AddStepStrip(new List<StripGroup> { new StripGroup("Soul", souls.Select(s => Step(SpriteOf(s.Stack.Element), s.Title, () => SendToOpenWindow(s)))) }, 40);
            }
            if (route.Towels)
            {
                List<ItemEntry> towels = _items.Where(i => i.Stack?.EntityId == "basket.towels" && !SlotCheck.InRunningRecipe(i)).Take(1).ToList();
                AddStepStrip(new List<StripGroup> { new StripGroup("Towels", towels.Select(i => Step(SpriteOf(i.Stack.Element), $"{i.Title} · adds Memory: Solace (optional)", () => SendOrFocus(i)))) }, 40);
            }
        }
        _count.text = $"{shown} ways · middle click a memory to take it";
    }

    private static readonly string[] Seasons = { "spring", "summer", "autumn", "winter" };

    /// <summary>
    /// Each day the weather verb deals one card from the season's deck (deck.weather.&lt;season&gt;,
    /// celestial_recipes_weather.json: daily.weather.*), and that card is a memory (Rain: Grail 2,
    /// Nectar 2...). The draw pile's order is fixed until it runs out, so the coming days are known.
    /// Only the weathers of the four season decks are listed (no special ones).
    /// </summary>
    private void RefreshWeatherMemories()
    {
        var specs = Seasons.ToDictionary(se => se, se => Watchman.Get<Compendium>().GetEntityById<DeckSpec>("deck.weather." + se));
        AspectsDictionary extant = Watchman.Get<HornedAxe>().GetAspectsInContext().AspectsExtant;
        string season = Seasons.FirstOrDefault(se => extant.AspectValue("season." + se) > 0);
        if (season != null)
        {
            List<Token> pile = new List<Token>();
            try
            {
                if (Watchman.Get<SecretHistories.Infrastructure.DealersTable>()?.GetDrawPile("deck.weather." + season) is SecretHistories.Spheres.Sphere sphere)
                    pile = sphere.GetElementTokens().ToList();
            }
            catch (Exception)
            {
                // no pile yet: the deck is created at the first draw
            }
            AddSectionLabel(pile.Count > 0
                ? $"Coming days ({char.ToUpper(season[0]) + season.Substring(1)}): tomorrow on the left"
                : "The season's weather deck reshuffles at the next draw", 0);
            if (pile.Count > 0)
                AddStepStrip(new List<StripGroup> { new StripGroup("", pile.Select((tok, n) => Step(SpriteOf(tok.PayloadEntityId),
                    $"Day {n + 1}: {WorldIndex.LabelOf(tok.PayloadEntityId)}", () => { }))) }, 0);
        }
        var weathers = specs.Values.Where(sp => sp?.Spec != null).SelectMany(sp => sp.Spec).Distinct()
            .Select(id => (id, element: WorldIndex.ElementOf(id)))
            .Where(w => w.element != null && MemoryLevelsMatch(w.element))
            .Where(w => string.IsNullOrEmpty(_query) || w.element.Label.IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderByDescending(w => _memoryLevels.Keys.Sum(pr => w.element.Aspects?.AspectValue(pr) ?? 0))
            .ThenBy(w => w.element.Label)
            .ToList();
        foreach (var w in weathers)
        {
            string where = string.Join(" · ", Seasons.Where(se => specs[se]?.Spec != null && specs[se].Spec.Contains(w.id))
                .Select(se => $"{char.ToUpper(se[0]) + se.Substring(1)} {specs[se].Spec.Count(c => c == w.id)}/{specs[se].Spec.Count}"));
            string note = MemoryNote(w.id, new List<ItemEntry>());
            Transform row = AddRow(SpriteOf(w.element), note != null ? $"<s>{w.element.Label}</s>" : w.element.Label, note ?? where,
                () => Watchman.Get<AureateNotifier>().ShowAspectDetails(w.element, SpriteOf(w.element)));
            if (note != null)
                Tint(row);
            if (row.Find("Art")?.GetComponent<Image>() is Image art)
            {
                art.raycastTarget = true;
                string id = w.id;
                art.gameObject.AddComponent<MiddleClick>().Click = () => GiveMemory(id, WorldIndex.LabelOf(id));
            }
            foreach (KeyValuePair<string, int> aspect in w.element.Aspects.Where(a => a.Value > 0 && WorldIndex.IsPrinciple(a.Key)).Take(5))
                AspectBadge(row, aspect.Key, aspect.Value, 22f, 16f);
        }
        _count.text = $"{weathers.Count} weathers · middle click one to take it";
    }

    /// <summary>Not in a locked room (fireplaces and wells exist in rooms not opened yet).</summary>
    private static bool Reachable(Token token)
    {
        SecretHistories.Tokens.Payloads.TerrainFeature room = WorldIndex.FindRoom(token, out _);
        return room == null || (!room.IsShrouded && !room.IsSealed);
    }

    /// <summary>
    /// Any memory card, not only mem.*: Old Moment, Occult Scrap, Earth-Sign, Wild Surmise... carry
    /// the "memory" aspect too (lessons are left out: they come from books and salons).
    /// </summary>
    private static bool IsMemoryCard(string id)
    {
        Element element = WorldIndex.ElementOf(id);
        return element?.Aspects != null && element.Aspects.AspectValue("memory") > 0 && element.Aspects.AspectValue("lesson") <= 0;
    }

    private static List<Recipe> _memoryCrafts;
    private static List<Recipe> _allCrafts;

    /// <summary>Every craft recipe with a skill requirement (any result), read once.</summary>
    private static List<Recipe> AllCrafts() => _allCrafts ??= Watchman.Get<Compendium>().GetEntitiesAsList<Recipe>()
        .Where(r => r?.Id != null && r.Craftable && r.Id.StartsWith("craft.", StringComparison.Ordinal) && r.Reqs != null && r.Effects != null
                    && r.Reqs.Keys.Any(k => k.StartsWith("s.", StringComparison.Ordinal)))
        .ToList();

    /// <summary>
    /// Craft recipes (crafting_*.json, craft.&lt;level&gt;.&lt;skill&gt;_&lt;ingredient&gt;_&lt;result&gt;_&lt;principle&gt;)
    /// at a library desk that make a memory card: a soul, the skill, one ingredient by aspect, and the
    /// principle at the level's amount (Prentice 5, Scholar 10, Keeper 15) over the whole desk.
    /// Read from the compendium once.
    /// </summary>
    private static List<Recipe> MemoryCrafts()
    {
        if (_memoryCrafts != null)
            return _memoryCrafts;
        _memoryCrafts = Watchman.Get<Compendium>().GetEntitiesAsList<Recipe>()
            .Where(r => r?.Id != null && r.Craftable && r.Id.StartsWith("craft.", StringComparison.Ordinal) && r.Effects != null
                        && r.Effects.Keys.Any(IsMemoryCard) && r.Reqs != null && r.Reqs.Keys.Any(k => k.StartsWith("s.", StringComparison.Ordinal)))
            .ToList();
        return _memoryCrafts;
    }

    /// <summary>
    /// Memories the player can craft: recipes whose skill card they hold. Per memory a header, per
    /// recipe a line "skill + ingredient + principle N", a verdict (can the best skill + soul +
    /// ingredient + memory + ink/tool reach it) with the easier recipe the desk falls back to, and
    /// steps Desk › Skill › Ingredient › Soul. An ingredient can be an aspect or a card id
    /// (confounding.parable): ItemEntry.Aspects leaves the element's own id out, so both are checked.
    /// </summary>
    private void RefreshCraftMemories()
    {
        var skillCards = _items.Where(i => i.Stack != null && i.Aspects.AspectValue("skill") > 0)
            .GroupBy(i => i.Stack.EntityId).ToDictionary(g => g.Key, g => g.First());
        Func<ItemEntry, bool> free = i => i.Token != null && i.Token.IsValid() && !SlotCheck.InRunningRecipe(i);
        // Memories a book on hand, an object in Consider, or the well / fire / sea already give are
        // left out: crafting them is never worth it, and every craft costs a desk check per desk.
        var easy = new HashSet<string>(_items.Where(i => i.Book?.Memory != null).Select(i => i.Book.Memory));
        easy.UnionWith(MemoryObjects().Keys);
        easy.UnionWith(PlaceRoutes.Select(r => r.Memory));
        easy.UnionWith(PlaceRoutes.Where(r => r.Also != null).Select(r => r.Also));
        var byMemory = MemoryCrafts()
            .Select(r => (recipe: r, memory: r.Effects.Keys.First(IsMemoryCard), skill: r.Reqs.Keys.First(k => k.StartsWith("s.", StringComparison.Ordinal))))
            .Where(x => skillCards.ContainsKey(x.skill) && !easy.Contains(x.memory))
            .GroupBy(x => x.memory)
            .Select(g => (memory: g.Key, element: WorldIndex.ElementOf(g.Key), crafts: g.ToList()))
            .Where(m => m.element != null && MemoryLevelsMatch(m.element))
            .Where(m => string.IsNullOrEmpty(_query) || WorldIndex.LabelOf(m.memory).IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderByDescending(m => _memoryLevels.Keys.Sum(pr => m.element.Aspects?.AspectValue(pr) ?? 0))
            .ThenBy(m => WorldIndex.LabelOf(m.memory), StringComparer.OrdinalIgnoreCase)
            .ToList();
        int rows = 0;
        foreach (var m in byMemory)
        {
            if (rows >= MaxRows)
                break;
            string note = MemoryNote(m.memory, new List<ItemEntry>());
            string label = WorldIndex.LabelOf(m.memory);
            bool persistent = m.element.Aspects.AspectValue("persistent") > 0;
            Transform row = AddRow(SpriteOf(m.element), note != null ? $"<s>{label}</s>" : label,
                note ?? (persistent ? "Persistent: survives the dawn" : "Craft at a desk"),
                () => Watchman.Get<AureateNotifier>().ShowAspectDetails(m.element, SpriteOf(m.element)));
            if (note != null)
                Tint(row);
            if (row.Find("Art")?.GetComponent<Image>() is Image art)
            {
                art.raycastTarget = true;
                string memoryId = m.memory;
                art.gameObject.AddComponent<MiddleClick>().Click = () => GiveMemory(memoryId, label);
            }
            foreach (KeyValuePair<string, int> aspect in m.element.Aspects.Where(a => a.Value > 0 && WorldIndex.IsPrinciple(a.Key))
                         .OrderByDescending(a => _memoryLevels.ContainsKey(a.Key)).ThenByDescending(a => a.Value).Take(5))
                AspectBadge(row, aspect.Key, aspect.Value, 22f, 16f);
            rows++;
            if (note != null)
                continue;
            // One formula row per requirement: recipes of this memory that differ only by the skill
            // (Ascendant Harmony: six skills, all "Instrument + Sky 10") share one row of icons.
            var checks = m.crafts.Select(c => CheckCraft(c.recipe, skillCards[c.skill], free)).ToList();
            foreach (var group in checks.GroupBy(c => (c.Ingredient, c.Principle, c.Need)))
            {
                CraftCheck best = group.OrderByDescending(c => c.Ok).ThenByDescending(c => c.Desks.Count > 0)
                    .ThenByDescending(c => c.Desks.Count > 0 ? c.Plans[c.Desks[0]].total : -1).First();
                AddCraftFormula(group.ToList(), best);
                rows++;
                if (best.Desks.Count == 0)
                    continue; // nothing to click: the status says what's missing
                string pl = WorldIndex.LabelOf(best.Principle);
                AddStepStrip(new List<StripGroup> { new StripGroup("Desk", best.Desks.Select(d =>
                {
                    var plan = best.Plans[d];
                    string check = plan.predicted == best.Recipe ? "game check OK" : "game would pick " + (plan.predicted?.Label ?? "nothing");
                    return Step(DeskArt(d), $"{d.Name} · {check} · {pl} {plan.total}: {plan.parts}", () => { OpenPlace(d); _lastOpenedDesk = d; RefreshKeepingScroll(); });
                })) }, 40);
                // Exactly the checked cards for the desk the player opened from the row (else the best
                // one): another desk's slots take other cards, so they come from that desk's own plan.
                DeskEntry shown = _lastOpenedDesk != null && best.Desks.Contains(_lastOpenedDesk) ? _lastOpenedDesk : best.Desks[0];
                var shownPlan = best.Plans[shown];
                string shownCheck = shownPlan.predicted == best.Recipe ? "game check OK" : "game would pick " + (shownPlan.predicted?.Label ?? "nothing");
                AddStepStrip(new List<StripGroup> { new StripGroup("Put in", shownPlan.cards.Select(i => Step(SpriteOf(i.Stack.Element),
                    $"{i.Title} · {pl} {i.Aspects.AspectValue(best.Principle)} · for {shown.Name} ({shownCheck})", () => SendOrFocus(i)))) }, 40);
                rows += 2;
            }
        }
        if (byMemory.Count == 0)
            AddSectionLabel("No memory you can craft with the skills you hold", 0);
        _count.text = $"{byMemory.Count} memories only crafts give · middle click one to take it";
    }

    /// <summary>One craft recipe checked against the player's cards and desks.</summary>
    private sealed class CraftCheck
    {
        public Recipe Recipe;
        public ItemEntry Skill;
        public string Ingredient;
        public string Principle;
        public int Need;
        /// <summary>The same skill's easier recipe the desk makes when this one isn't met.</summary>
        public Recipe Fallback;
        public bool HaveIngredient;
        public Dictionary<DeskEntry, (int total, bool ingredientFits, string parts, Recipe predicted, List<ItemEntry> cards)> Plans;
        /// <summary>Desks that take the skill and the ingredient, the game-checked ones first.</summary>
        public List<DeskEntry> Desks;
        public bool Ok;
        /// <summary>A few words: "ready at Desk", "no Instrument", "Sky 7 of 10"...</summary>
        public string Status;
    }

    private static readonly Color CraftOk = new Color(0.61f, 0.8f, 0.48f);
    private static readonly Color CraftBad = new Color(0.85f, 0.47f, 0.42f);

    private CraftCheck CheckCraft(Recipe recipe, ItemEntry skill, Func<ItemEntry, bool> free)
    {
        var c = new CraftCheck { Recipe = recipe, Skill = skill, Desks = new List<DeskEntry>() };
        string skillId = skill.Stack.EntityId;
        KeyValuePair<string, string> pReq = recipe.Reqs.FirstOrDefault(r => WorldIndex.IsPrinciple(r.Key));
        c.Principle = pReq.Key;
        int.TryParse(pReq.Value, out c.Need);
        c.Ingredient = recipe.Reqs.Keys.FirstOrDefault(k => k != "ability" && k != skillId && !WorldIndex.IsPrinciple(k));
        string p = c.Principle;
        int need = c.Need;
        // The same skill's easier recipe (fewer reqs, lower amount of the same principle) wins
        // whenever this one isn't met: the desk then makes that instead (e.g. Edicts Martial:
        // Moon 15 + Confounding Parable -> Forbidden Epic, but Moon 5 alone -> Old Wound).
        c.Fallback = p == null ? null : AllCrafts()
            .Where(r => r != recipe && r.Reqs.ContainsKey(skillId) && r.Reqs.TryGetValue(p, out string v)
                        && int.TryParse(v, out int n) && n < need
                        && r.Reqs.Keys.All(k => recipe.Reqs.ContainsKey(k)))
            .OrderByDescending(r => int.Parse(r.Reqs[p]))
            .FirstOrDefault();
        c.HaveIngredient = c.Ingredient == null || _items.Any(i => free(i) && (i.Aspects.AspectValue(c.Ingredient) > 0 || i.Stack.EntityId == c.Ingredient)
                                                                && i.Aspects.AspectValue("skill") <= 0);
        List<DeskEntry> desks = _desks.Where(d => d.IsIdle && d.Situation?.VerbId != null && d.Situation.VerbId.StartsWith("library.", StringComparison.Ordinal)
                && SlotCheck.Slot(d, "skill") is SphereSpec ss && SlotCheck.Takes(ss, skill))
            .Take(16).ToList();
        // Per desk, what it can really hold: each slot takes only some cards (the desk's own
        // principles), so the best soul overall may not fit (DeskCraftPlan).
        c.Plans = desks.ToDictionary(d => d, d => DeskCraftPlan(d, recipe, skill, c.Ingredient, p, need, free));
        if (p == null)
        {
            c.Status = "no principle to reach";
            return c;
        }
        c.Desks = c.Plans.Where(kv => kv.Value.total >= 0 && kv.Value.ingredientFits)
            .OrderByDescending(kv => kv.Value.predicted == recipe).ThenByDescending(kv => kv.Value.total).Select(kv => kv.Key).ToList();
        string pl = WorldIndex.LabelOf(p);
        if (!c.HaveIngredient)
            c.Status = $"no {WorldIndex.LabelOf(c.Ingredient)}";
        else if (c.Desks.Count == 0)
            c.Status = "no free desk takes it";
        else
        {
            DeskEntry desk = c.Desks[0];
            var plan = c.Plans[desk];
            c.Ok = plan.predicted == recipe;
            if (c.Ok)
                c.Status = $"ready · {desk.Name}";
            else if (plan.total >= need)
                c.Status = $"{desk.Name} would make {(plan.predicted != null ? plan.predicted.Label : "nothing")}";
            else
                c.Status = $"{pl} {plan.total} of {need} at best";
        }
        return c;
    }

    /// <summary>
    /// A craft as one row of icons: [skills] + [ingredient] + [principle] N   status. Skills that
    /// can't do it right now are dimmed, a missing ingredient is red. Hover says the details
    /// (what the skill gives, what the desk makes below the amount); a skill click sends it.
    /// </summary>
    private void AddCraftFormula(List<CraftCheck> group, CraftCheck best)
    {
        Image row = GameStyle.Panel(_results, "Formula", "slots_bg", new Color(1f, 1f, 1f, 0.2f));
        GameStyle.Size(row, height: 42f);
        GameStyle.Layout<HorizontalLayoutGroup>(row, 5f, new RectOffset(28, 8, 4, 4), TextAnchor.MiddleLeft);
        string p = best.Principle;
        string pl = p != null ? WorldIndex.LabelOf(p) : "";
        bool anyOk = group.Any(c => c.Ok);
        foreach (CraftCheck c in group)
        {
            CraftCheck captured = c;
            string tip = $"{c.Skill.Title} · gives {pl} {c.Skill.Aspects.AspectValue(p ?? "")}"
                         + (c.Fallback != null ? $" · below {c.Need} the desk makes {WorldIndex.LabelOf(c.Fallback.Effects.Keys.First())} ({pl} {c.Fallback.Reqs[p]})" : "")
                         + $" · {c.Status}";
            FormulaIcon(row.transform, SpriteOf(c.Skill.Stack.Element), anyOk && !c.Ok ? new Color(1f, 1f, 1f, 0.45f) : Color.white,
                tip, () => SendToOpenWindow(captured.Skill));
        }
        if (best.Ingredient != null)
        {
            FormulaSign(row.transform, "+");
            string il = WorldIndex.LabelOf(best.Ingredient);
            FormulaIcon(row.transform, SpriteOf(best.Ingredient), best.HaveIngredient ? Color.white : new Color(1f, 0.4f, 0.35f, 0.6f),
                best.HaveIngredient ? $"Any card with {il}" : $"Any card with {il} · you have none", null);
        }
        if (p != null)
        {
            FormulaSign(row.transform, "+");
            FormulaIcon(row.transform, SpriteOf(p), Color.white, $"{pl} {best.Need} in total over the desk: soul, skill and the other cards", null);
            GameStyle.Size(GameStyle.Text(row.transform, "Need", best.Need.ToString(), 18f, GameStyle.Cream, FontStyles.Bold), 26f, 32f);
        }
        TextMeshProUGUI status = GameStyle.Text(row.transform, "Status", best.Status, 14f, best.Ok ? CraftOk : CraftBad, FontStyles.Italic, TextAlignmentOptions.MidlineRight);
        GameStyle.Size(status, height: 32f, flexibleWidth: 1f);
    }

    private void FormulaSign(Transform row, string sign) =>
        GameStyle.Size(GameStyle.Text(row, "Sign", sign, 18f, GameStyle.GoldFaint, FontStyles.Bold, TextAlignmentOptions.Center), 12f, 32f);

    private void FormulaIcon(Transform row, Sprite sprite, Color tint, string tooltip, Action click)
    {
        Image icon = GameStyle.Panel(row, "Icon", "", tint);
        icon.sprite = sprite;
        icon.preserveAspect = true;
        GameStyle.Size(icon, 32f, 32f);
        if (click != null)
        {
            Button button = icon.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.4f, 1.3f, 1f, 1f);
            colors.pressedColor = new Color(1.8f, 1.6f, 1.1f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => click());
        }
        var hover = icon.gameObject.AddComponent<HoverHandler>();
        string before = null;
        hover.Enter = () => { before = _count.text; _count.text = tooltip; };
        hover.Exit = () => { if (before != null) _count.text = before; };
    }

    /// <summary>
    /// What one desk can bring to a craft: -1 if its skill slot refuses the skill. The skill and the
    /// ingredient first; then, while the amount isn't reached, each free slot gets the best card it
    /// takes - but never a card that would let an earlier recipe of the same skill fire instead (the
    /// game runs the first craftable recipe whose reqs are met, in load order; an extra memory can
    /// turn a craft into another one). Fresh souls only, no readables (a book makes it a reading).
    /// "parts" says which card gave what.
    /// </summary>
    private (int total, bool ingredientFits, string parts, Recipe predicted, List<ItemEntry> cards) DeskCraftPlan(DeskEntry desk, Recipe target, ItemEntry skill, string ingredient, string p, int need, Func<ItemEntry, bool> free)
    {
        List<SphereSpec> slots = desk.Situation?.Verb?.Slots ?? new List<SphereSpec>();
        SphereSpec skillSlot = slots.FirstOrDefault(sl => sl.Essential != null && sl.Essential.ContainsKey("skill") && SlotCheck.Takes(sl, skill));
        if (skillSlot == null || p == null)
            return (-1, false, "", null, new List<ItemEntry>());
        // Recipes the desk tries before the target, in the game's own order (Priority, then load order).
        List<Recipe> order = Watchman.Get<Compendium>().GetValidRecipesForUnstartedVerb(desk.Situation.VerbId).ToList();
        int targetIndex = order.IndexOf(target);
        List<Recipe> earlier = order.Take(targetIndex < 0 ? 0 : targetIndex)
            .Where(r => r.Reqs != null && r.Reqs.ContainsKey(skill.Stack.EntityId)).ToList();
        var chosen = new List<ItemEntry> { skill };
        var used = new HashSet<SphereSpec> { skillSlot };
        bool fits = ingredient == null;
        if (ingredient != null)
        {
            var best = slots.Where(sl => !used.Contains(sl))
                .SelectMany(sl => _items.Where(i => free(i) && (i.Aspects.AspectValue(ingredient) > 0 || i.Stack.EntityId == ingredient)
                                                    && i.Aspects.AspectValue("skill") <= 0 && SlotCheck.Takes(sl, i)).Select(i => (sl, i)))
                .OrderByDescending(x => x.i.Aspects.AspectValue(p)).FirstOrDefault();
            if (best.i != null)
            {
                used.Add(best.sl);
                chosen.Add(best.i);
                fits = true;
            }
        }
        List<ItemEntry> others = _items.Where(i => free(i) && !chosen.Contains(i) && i.Aspects.AspectValue(p) > 0 && i.Aspects.AspectValue("skill") <= 0
                && i.Aspects.AspectValue("readable") <= 0 && i.Aspects.AspectValue("fatigued") <= 0 && i.Aspects.AspectValue("malady") <= 0)
            .OrderByDescending(i => i.Aspects.AspectValue(p)).ToList();
        foreach (SphereSpec sl in slots.Where(sl => !used.Contains(sl)))
        {
            if (chosen.Sum(i => i.Aspects.AspectValue(p)) >= need)
                break; // enough: fewer cards, fewer chances to wake another recipe
            ItemEntry pick = others.FirstOrDefault(i => SlotCheck.Takes(sl, i) && !earlier.Any(r => ReqsMet(r, chosen.Append(i))));
            if (pick == null)
                continue;
            others.Remove(pick);
            chosen.Add(pick);
        }
        // Every craft needs a soul: the plainest the desk takes if none came in for the principle.
        if (!chosen.Any(i => i.Aspects.AspectValue("ability") > 0)
            && slots.FirstOrDefault(sl => sl.Essential != null && sl.Essential.ContainsKey("ability")) is SphereSpec soulSlot)
        {
            ItemEntry soul = _items.Where(i => free(i) && i.Aspects.AspectValue("ability") > 0 && i.Aspects.AspectValue("fatigued") <= 0
                    && i.Aspects.AspectValue("malady") <= 0 && SlotCheck.Takes(soulSlot, i) && !earlier.Any(r => ReqsMet(r, chosen.Append(i))))
                .OrderBy(i => i.Aspects.AspectValue("rank")).FirstOrDefault();
            if (soul != null)
                chosen.Add(soul);
        }
        int total = chosen.Sum(i => i.Aspects.AspectValue(p));
        string parts = string.Join(" + ", chosen.Select(i => (i == skill ? "skill" : i.Title) + " " + i.Aspects.AspectValue(p)));
        return (total, fits, parts, PredictRecipe(desk, chosen), chosen);
    }

    /// <summary>
    /// The recipe this desk would offer with exactly these cards in it, asked of the game itself:
    /// Compendium.GetFirstMatchingRecipe over the cards' aspects plus the desk verb's own, as the
    /// unstarted desk does (UnstartedState). Null when nothing matches.
    /// </summary>
    private static Recipe PredictRecipe(DeskEntry desk, IEnumerable<ItemEntry> cards)
    {
        try
        {
            var local = new AspectsDictionary();
            foreach (ItemEntry c in cards)
                local.CombineAspects(c.Stack.GetAspects(true));
            if (desk.Situation.Verb.Aspects != null)
                local.CombineAspects(desk.Situation.Verb.Aspects);
            AspectsInContext context = Watchman.Get<HornedAxe>().GetAspectsInContext(local);
            return Watchman.Get<Compendium>().GetFirstMatchingRecipe(context, desk.Situation.VerbId);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>A recipe's reqs against a set of cards (their aspects plus their own ids), as the game checks them.</summary>
    private static bool ReqsMet(Recipe recipe, IEnumerable<ItemEntry> cards)
    {
        var sum = new Dictionary<string, int>();
        foreach (ItemEntry c in cards)
        {
            foreach (KeyValuePair<string, int> a in c.Aspects)
                sum[a.Key] = (sum.TryGetValue(a.Key, out int v) ? v : 0) + a.Value;
            sum[c.Stack.EntityId] = (sum.TryGetValue(c.Stack.EntityId, out int w) ? w : 0) + 1;
        }
        sum["ability"] = Math.Max(sum.TryGetValue("ability", out int ab) ? ab : 0, 1); // the desk always has a soul
        foreach (KeyValuePair<string, string> req in recipe.Reqs)
        {
            if (!int.TryParse(req.Value, out int n))
                continue;
            int have = sum.TryGetValue(req.Key, out int h) ? h : 0;
            if (n > 0 ? have < n : have >= -n)
                return false;
        }
        return true;
    }

    private static readonly Color Missing = new Color(1f, 0.4f, 0.35f, 0.55f);

    /// <summary>
    /// Kitchen recipes the player has cooked (CookBook), ready ones first. Per recipe the dish, what
    /// is missing, and one strip: kitchen › soul › each ingredient (the cards on hand, or its icon in
    /// red). A missing ingredient another recipe makes (Cake Batter for a cake) gets that recipe as
    /// an earlier step, the first step on top.
    /// </summary>
    private void RefreshCookbook()
    {
        Func<ItemEntry, bool> free = i => i.Token != null && i.Token.IsValid() && !SlotCheck.InRunningRecipe(i);
        List<DeskEntry> kitchens = _desks.Where(d => d.Situation?.VerbId != null && d.Situation.VerbId.StartsWith("library.kitchen.", StringComparison.Ordinal))
            .OrderBy(d => d.IsIdle ? 0 : 1).ToList();
        HashSet<string> cooked = CookBook.Cooked();
        var entries = CookBook.Entries()
            .Select(e => (entry: e, dish: WorldIndex.ElementOf(e.Results.FirstOrDefault() ?? ""), missing: e.Ingredients.Where(k => CookCards(k, free).Count == 0).ToList()))
            .Where(x => _cookKind == 0 || (x.dish?.Aspects != null && x.dish.Aspects.AspectValue(_cookKind == 1 ? "sustenance" : "beverage") > 0))
            .Where(x => _cookCourse == null || (x.dish?.Aspects?.AspectValue(_cookCourse) ?? 0) > 0)
            .Where(x => _cookLevels.All(l => (x.dish?.Aspects?.AspectValue(l.Key) ?? 0) >= l.Value))
            .Where(x => string.IsNullOrEmpty(_query) || x.entry.Label.IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0
                        || x.entry.Results.Concat(x.entry.Ingredients).Any(k => WorldIndex.LabelOf(k).IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0))
            .OrderBy(x => x.missing.Count)
            .ThenByDescending(x => _cookLevels.Keys.Sum(p => x.dish?.Aspects?.AspectValue(p) ?? 0))
            .ThenBy(x => x.entry.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
        int rows = 0;
        foreach (var x in entries)
        {
            if (rows >= MaxRows)
                break;
            List<string> results = x.entry.Results;
            Element dish = x.dish;
            string subtitle = (x.missing.Count == 0 ? "<color=#9CCB7A>Ready to make</color>"
                                  : "<color=#D9796A>Missing: " + string.Join(", ", x.missing.Select(k => WorldIndex.LabelOf(ShownIngredient(k)))) + "</color>")
                              + CourseText(dish)
                              + (results.Count > 1 ? " · also " + string.Join(", ", results.Skip(1).Select(WorldIndex.LabelOf)) : "")
                              + (x.entry.OnlyThese ? " · nothing else edible in the pot" : "");
            Transform row = AddRow(SpriteOf(dish), x.entry.Label, subtitle,
                () => { if (dish != null) Watchman.Get<AureateNotifier>().ShowAspectDetails(dish, SpriteOf(dish)); });
            if (dish != null && row.Find("Art")?.GetComponent<Image>() is Image art)
            {
                art.raycastTarget = true;
                art.gameObject.AddComponent<MiddleClick>().Click = () => GiveToPortage(dish.Id, dish.Label);
            }
            if (dish?.Aspects != null)
                foreach (KeyValuePair<string, int> aspect in dish.Aspects.Where(a => a.Value > 0 && WorldIndex.IsPrinciple(a.Key))
                             .OrderByDescending(a => _cookLevels.ContainsKey(a.Key)).ThenByDescending(a => a.Value).Take(5))
                    AspectBadge(row, aspect.Key, aspect.Value, 22f, 16f);
            rows++;
            if (x.entry.Recipe != null)
            {
                // Earlier steps for missing ingredients the kitchen itself makes, the deepest first.
                var chain = new List<Recipe>();
                CollectCookSteps(x.entry.Recipe, free, chain, 0);
                for (int i = 0; i < chain.Count; i++)
                {
                    AddCookStepHeader(i + 1, chain[i]);
                    AddCookStrip(CookBook.Ingredients(chain[i]), kitchens, free);
                    rows += 2;
                }
                if (chain.Count > 0)
                    AddCookStepHeader(chain.Count + 1, x.entry.Recipe);
            }
            AddCookStrip(x.entry.Ingredients, kitchens, free);
            rows++;
        }
        if (entries.Count == 0)
            AddSectionLabel(cooked.Count == 0 ? "Nothing cooked yet: dishes appear here once cooked in a kitchen" : "No dish matches", 0);
        _count.text = $"{entries.Count} dishes and brews · {entries.Count(x => x.missing.Count == 0)} ready now";
    }

    /// <summary>" · First, Side": the courses the dish counts as at a formal meal.</summary>
    private static string CourseText(Element dish)
    {
        if (dish?.Aspects == null)
            return "";
        string[] courses = Courses.Where(c => dish.Aspects.AspectValue(c.Aspect) > 0).Select(c => c.Label).ToArray();
        return courses.Length == 0 ? "" : " · " + string.Join(", ", courses);
    }

    /// <summary>
    /// "Step 1: Mix Cake Batter → [icon] Cake Batter": what each step makes, the icon clickable for
    /// the card's details (several icons when a recipe makes more, e.g. a roast and dripping).
    /// </summary>
    private void AddCookStepHeader(int step, Recipe recipe)
    {
        RectTransform row = GameStyle.Rect("StepHeader", _results);
        GameStyle.Size(row, height: 30f);
        GameStyle.Layout<HorizontalLayoutGroup>(row, 6f, new RectOffset(48, 8, 2, 2), TextAnchor.MiddleLeft);
        List<string> results = CookBook.Results(recipe);
        GameStyle.Size(GameStyle.Text(row, "Step", $"Step {step}: {recipe.Label}  →", 14f, GameStyle.Gold, FontStyles.Bold | FontStyles.SmallCaps), height: 26f)
            .flexibleWidth = 0f;
        foreach (string id in results)
        {
            Element element = WorldIndex.ElementOf(id);
            Image icon = GameStyle.Panel(row, "Result", "", Color.white);
            icon.sprite = SpriteOf(element);
            icon.preserveAspect = true;
            GameStyle.Size(icon, 26f, 26f);
            Button button = icon.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.4f, 1.3f, 1f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => { if (element != null) Watchman.Get<AureateNotifier>().ShowAspectDetails(element, SpriteOf(element)); });
            icon.gameObject.AddComponent<MiddleClick>().Click = () => GiveToPortage(id, WorldIndex.LabelOf(id));
            GameStyle.Size(GameStyle.Text(row, "Name", WorldIndex.LabelOf(id), 14f, GameStyle.Cream, FontStyles.Normal), height: 26f);
        }
    }

    /// <summary>
    /// A card to give for a missing requirement (middle click): the card itself when the requirement
    /// is a card id (honey, dough.cake), else the plainest ingredient carrying that aspect (egg ->
    /// a hen's egg, intoxicating -> some wine): fewest principles, no unique or raw-only oddities.
    /// </summary>
    /// <summary>
    /// What a requirement really means, for display: when it's an aspect that only one actual
    /// ingredient can meet (egg: brewable eggs hatch, so only a hen's egg), that card; else the
    /// requirement itself.
    /// </summary>
    private static string ShownIngredient(string req)
    {
        Element own = WorldIndex.ElementOf(req);
        if (own != null && !own.IsAspect)
            return req;
        if (!_shownIngredients.TryGetValue(req, out string shown))
        {
            List<string> valid = Watchman.Get<Compendium>().GetEntitiesAsList<Element>()
                .Where(e => e?.Id != null && !e.IsAspect && !e.Id.StartsWith("_", StringComparison.Ordinal) && e.Aspects != null
                            && e.Aspects.AspectValue(req) > 0 && e.Aspects.AspectValue("ingredient") > 0
                            && !(e.Aspects.AspectValue("egg") > 0 && e.Aspects.AspectValue("brewable") > 0))
                .Select(e => e.Id).Take(2).ToList();
            _shownIngredients[req] = shown = valid.Count == 1 ? valid[0] : req;
        }
        return shown;
    }

    private static readonly Dictionary<string, string> _shownIngredients = new Dictionary<string, string>();

    private static string IngredientCard(string req)
    {
        Element own = WorldIndex.ElementOf(req);
        if (own != null && !own.IsAspect)
            return req;
        return Watchman.Get<Compendium>().GetEntitiesAsList<Element>()
            .Where(e => e?.Id != null && !e.IsAspect && !e.Id.StartsWith("_", StringComparison.Ordinal) && e.Aspects != null
                        && e.Aspects.AspectValue(req) > 0 && e.Aspects.AspectValue("ingredient") > 0 && !e.Unique)
            .OrderBy(e => e.Aspects.Where(a => WorldIndex.IsPrinciple(a.Key)).Sum(a => a.Value))
            .Select(e => e.Id)
            .FirstOrDefault();
    }

    private void CollectCookSteps(Recipe recipe, Func<ItemEntry, bool> free, List<Recipe> chain, int depth)
    {
        if (depth >= 3)
            return;
        foreach (string k in CookBook.Ingredients(recipe).Where(k => CookCards(k, free).Count == 0))
        {
            Recipe maker = CookBook.MakerOf(k);
            if (maker == null || chain.Contains(maker) || maker == recipe)
                continue;
            CollectCookSteps(maker, free, chain, depth + 1);
            chain.Add(maker);
        }
    }

    /// <summary>
    /// Free cards for one requirement (a card id or an aspect), one of each kind; souls and skills
    /// aside. Brewable eggs (gull, viper, the outsized one) are left out: in a kitchen they match
    /// hatch.egg.gull.kitchen (egg + brewable) and hatch instead of cooking - only a hen's egg cooks.
    /// </summary>
    private List<ItemEntry> CookCards(string req, Func<ItemEntry, bool> free) => _items
        .Where(i => i.Stack != null && free(i) && (i.Stack.EntityId == req || i.Aspects.AspectValue(req) > 0)
                    && i.Aspects.AspectValue("ability") <= 0 && i.Aspects.AspectValue("skill") <= 0
                    && !(i.Aspects.AspectValue("egg") > 0 && i.Aspects.AspectValue("brewable") > 0))
        .GroupBy(i => i.Stack.EntityId).Select(g => g.First())
        .OrderBy(i => i.Stack.EntityId == req ? 0 : 1).ThenBy(i => i.Title)
        .Take(5).ToList();

    private void AddCookStrip(List<string> ingredients, List<DeskEntry> kitchens, Func<ItemEntry, bool> free)
    {
        var groups = new List<StripGroup>
        {
            new StripGroup("Cook", kitchens.Select(d => Step(DeskArt(d), $"{d.Name} · {d.Location}" + (d.IsIdle ? "" : " · busy"),
                () => { OpenPlace(d); _lastOpenedDesk = d; }))),
        };
        // Any fresh soul the kitchen's soul slot takes: the plainest, it's only fatigued.
        DeskEntry kitchen = kitchens.FirstOrDefault(d => d.IsIdle) ?? kitchens.FirstOrDefault();
        ItemEntry soul = _items.Where(i => free(i) && i.Aspects.AspectValue("ability") > 0 && i.Aspects.AspectValue("fatigued") <= 0
                                           && i.Aspects.AspectValue("malady") <= 0
                                           && (kitchen == null || !(SlotCheck.Slot(kitchen, "ability") is SphereSpec sl) || SlotCheck.Takes(sl, i)))
            .OrderBy(i => i.Aspects.AspectValue("rank")).FirstOrDefault();
        groups.Add(new StripGroup("", soul != null
            ? new[] { Step(SpriteOf(soul.Stack.Element), $"{soul.Title} · any fresh soul", () => SendToOpenWindow(soul)) }
            : new[] { new StripStep { Icon = SpriteOf("ability"), Tint = Missing, Tooltip = "No fresh soul free", Click = () => { } } }));
        foreach (string k in ingredients)
        {
            List<ItemEntry> cards = CookCards(k, free);
            string label = WorldIndex.LabelOf(ShownIngredient(k));
            if (cards.Count > 0)
            {
                groups.Add(new StripGroup("", cards.Select(i =>
                {
                    StripStep step = Step(SpriteOf(i.Stack.Element),
                        $"{label}: {i.Title}" + (i.Location.Length > 0 ? " · " + i.Location : ""), () => SendOrFocus(i));
                    step.Middle = () => GiveToPortage(i.Stack.EntityId, i.Title);
                    return step;
                })));
                continue;
            }
            Element element = WorldIndex.ElementOf(ShownIngredient(k));
            Recipe maker = CookBook.MakerOf(k);
            string give = IngredientCard(k);
            groups.Add(new StripGroup("", new[] { new StripStep
            {
                Icon = SpriteOf(element), Tint = Missing,
                Tooltip = $"No {label}" + (maker != null ? $" · make it first: {maker.Label}" : ""),
                Click = () => { if (element != null) Watchman.Get<AureateNotifier>().ShowAspectDetails(element, SpriteOf(element)); },
                Middle = give == null ? null : () => GiveToPortage(give, WorldIndex.LabelOf(give)),
            } }));
        }
        AddStepStrip(groups, 40);
    }

    /// <summary>The single Consider verb on the table (not a desk's "...consider").</summary>
    private static Situation ConsiderVerb() =>
        Watchman.Get<HornedAxe>().GetRegisteredSituations().FirstOrDefault(s => s.VerbId == "consider");

    /// <summary>The memory row, its books and objects; returns the number of rows added.</summary>
    private int AddMemoryRows(string memory, Element element, List<ItemEntry> books, List<ItemEntry> objects, string note)
    {
        string label = WorldIndex.LabelOf(memory);
        bool taken = note != null;
        // No "From N books" line: the book rows right below show it.
        Transform row = AddRow(SpriteOf(element), taken ? $"<s>{label}</s>" : label, note ?? "",
            () => Watchman.Get<AureateNotifier>().ShowAspectDetails(element, SpriteOf(element)));
        if (taken)
            Tint(row);
        // Middle click on the memory's picture: the card straight into the hand (a shortcut past
        // the minute-long reread, at the player's wish).
        if (row.Find("Art")?.GetComponent<Image>() is Image art)
        {
            art.raycastTarget = true;
            art.gameObject.AddComponent<MiddleClick>().Click = () => GiveMemory(memory, label);
        }
        if (element.Aspects != null)
        {
            foreach (KeyValuePair<string, int> aspect in element.Aspects
                         .Where(a => a.Value > 0 && WorldIndex.IsPrinciple(a.Key))
                         .OrderByDescending(a => _memoryLevels.ContainsKey(a.Key))
                         .ThenByDescending(a => a.Value)
                         .Take(5))
                AspectBadge(row, aspect.Key, aspect.Value, 22f, 16f);
        }
        // Already on its way or held: nothing to do, no steps.
        if (taken)
            return 1;
        int added = 1;
        if (books.Count > 0)
            added += AddBookMemorySteps(element, books);
        if (objects.Count > 0)
            added += AddObjectMemorySteps(element, objects);
        return added;
    }

    /// <summary>
    /// Examine an object in Consider: the verb, the object (tooltip says whether it stays), then
    /// any soul ("Effort": any Soul card), plainest first, sparing souls of the memory's principles.
    /// </summary>
    private int AddObjectMemorySteps(Element element, List<ItemEntry> objects)
    {
        Situation consider = ConsiderVerb();
        var considerSteps = new List<StripStep>();
        if (consider != null)
            considerSteps.Add(Step(SafeArt(consider), "Consider", () =>
            {
                Focus(consider.Token, consider, "Consider");
                if (consider.Token != null && consider.Token.IsValid())
                    consider.OpenAt(consider.Token.Location);
            }));
        List<string> memoryPrinciples = element.Aspects == null ? new List<string>()
            : element.Aspects.Where(a => a.Value > 0 && WorldIndex.IsPrinciple(a.Key)).Select(a => a.Key).ToList();
        List<ItemEntry> souls = _items.Where(s => s.Aspects.AspectValue("ability") > 0 && s.Aspects.AspectValue("fatigued") <= 0
                && !SlotCheck.InRunningRecipe(s))
            .GroupBy(s => s.Stack.EntityId).Select(g => g.First())
            .OrderBy(s => memoryPrinciples.Sum(p => Math.Min(s.Aspects.AspectValue(p), 1)))
            .ThenBy(s => s.Aspects.AspectValue("rank")).ThenBy(s => s.Title)
            .Take(6).ToList();
        AddStepStrip(new List<StripGroup> { new StripGroup("Consider", considerSteps) }, 40);
        AddStepStrip(new List<StripGroup> { new StripGroup("Object", objects.Take(16).Select(o => Step(SpriteOf(o.Stack.Element),
            $"{o.Title} · {(UsedUpInConsider(o) ? "used up" : "stays")}{(o.Location.Length > 0 ? " · " + o.Location : "")}",
            () => SendOrFocus(o)))) }, 40);
        AddStepStrip(new List<StripGroup> { new StripGroup("Soul", souls.Select(s => Step(SpriteOf(s.Stack.Element),
            s.Stack.Quantity > 1 ? $"{s.Title} ×{s.Stack.Quantity}" : s.Title, () => SendToOpenWindow(s)))) }, 40);
        return 3;
    }

    private static Sprite SafeArt(Situation situation)
    {
        try
        {
            return situation.GetDetailSprite();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private int AddBookMemorySteps(Element element, List<ItemEntry> books)
    {
        // How to make it: reread one of these books at a desk with any soul the desk takes (the
        // "<Mystery>: Secured" recipe needs no principle). One row: Desk › Book › Soul.
        List<ItemEntry> freeBooks = books.Where(b => !SlotCheck.InRunningRecipe(b)).ToList();
        List<DeskEntry> desks = _desks.Where(d => d.IsIdle && d.Situation?.VerbId != null
                && d.Situation.VerbId.EndsWith(".consider", StringComparison.Ordinal)
                && SlotCheck.Slot(d, "ability") != null
                && freeBooks.Any(b => SlotCheck.OtherSlotTakes(d, b)))
            .Take(16).ToList(); // own row: as many as fit
        // Souls for the desk open right now if there is one (else for the desks above), plainest
        // first: rereading needs no principle, so spend un-evolved souls (low "rank": Health 12,
        // +Health 22...) and keep the evolved ones.
        List<string> memoryPrinciples = element.Aspects == null ? new List<string>()
            : element.Aspects.Where(a => a.Value > 0 && WorldIndex.IsPrinciple(a.Key)).Select(a => a.Key).ToList();
        DeskEntry openDesk = _lastOpenedDesk != null && _lastOpenedDesk.Situation != null && _lastOpenedDesk.Situation.IsOpen
            ? _lastOpenedDesk
            : _desks.FirstOrDefault(d => d.Situation != null && d.Situation.IsOpen && SlotCheck.Slot(d, "ability") != null);
        List<DeskEntry> soulDesks = openDesk != null ? new List<DeskEntry> { openDesk } : desks;
        List<ItemEntry> souls = _items.Where(s => s.Aspects.AspectValue("ability") > 0 && s.Aspects.AspectValue("fatigued") <= 0
                && !SlotCheck.InRunningRecipe(s) && soulDesks.Any(d => SlotCheck.Takes(SlotCheck.Slot(d, "ability"), s)))
            .GroupBy(s => s.Stack.EntityId).Select(g => g.First())
            // A soul sharing a principle with this memory is wanted for the same kind of work
            // later (a Moon memory and a Moon soul both serve Moon tasks): spend the others first.
            .OrderBy(s => memoryPrinciples.Sum(p => Math.Min(s.Aspects.AspectValue(p), 1)))
            .ThenBy(s => s.Aspects.AspectValue("rank")).ThenBy(s => s.Title)
            .Take(6).ToList();
        // One row per step, so a long list of books has a row to itself (up to 16 fit the width).
        // Opening a desk redraws the list, so the Soul row follows that desk.
        AddStepStrip(new List<StripGroup> { new StripGroup("Desk", desks.Select(d => Step(DeskArt(d), $"{d.Name} · {d.Location}", () => { OpenPlace(d); _lastOpenedDesk = d; RefreshKeepingScroll(); }))) }, 40);
        AddStepStrip(new List<StripGroup> { new StripGroup("Book", freeBooks.Take(16).Select(book => Step(SpriteOf(book.Stack.Element), $"{book.Title} · {book.Location}", () => SendOrFocus(book)))) }, 40);
        AddStepStrip(new List<StripGroup> { new StripGroup("Soul", souls.Select(s => Step(SpriteOf(s.Stack.Element),
            s.Stack.Quantity > 1 ? $"{s.Title} ×{s.Stack.Quantity}" : s.Title, () => SendToOpenWindow(s)))) }, 40);
        return 3;
    }

    /// <summary>Puts one card of <paramref name="memory"/> into the hand's memories row.</summary>
    private void GiveMemory(string memory, string label)
    {
        SecretHistories.Spheres.Sphere hand = Watchman.Get<HornedAxe>().GetSpheres()
            .FirstOrDefault(sp => sp != null && !sp.Defunct && sp.GetAbsolutePath().ToString().EndsWith("hand.memories", StringComparison.Ordinal));
        if (hand == null)
        {
            _count.text = "No hand found";
            return;
        }
        hand.ProvisionElementToken(memory, 1);
        _log.LogInfo($"Gave {memory}");
        Rescan(); // it now shows as held
        _count.text = $"Added to hand: {label}";
    }

    /// <summary>
    /// Cheat (middle click on a dish in the Cookbook): a new card in the first empty slot of the
    /// player's carried things - the five portage spheres, ~/portage1..5.
    /// </summary>
    private void GiveToPortage(string elementId, string label)
    {
        HornedAxe axe = Watchman.Get<HornedAxe>();
        SecretHistories.Spheres.Sphere slot = Enumerable.Range(1, 5)
            .Select(n => axe.GetSphereByAbsolutePath(new SecretHistories.Fucine.FucinePath("~/portage" + n)))
            .FirstOrDefault(sp => sp != null && !sp.Defunct && sp.GetElementTokens().Count == 0);
        if (slot == null)
        {
            _count.text = "No free slot in your things (five slots, all taken)";
            return;
        }
        slot.ProvisionElementToken(elementId, 1);
        _log.LogInfo($"Gave {elementId} into {slot.Id}");
        float y = _results.anchoredPosition.y;
        Rescan(); // the new card counts as on hand: ingredients and "ready" follow
        StartCoroutine(RestoreScroll(y));
        _count.text = $"Added to your things: {label}";
    }

    /// <summary>The desk last opened from a step row: the Soul row follows it.</summary>
    private DeskEntry _lastOpenedDesk;

    /// <summary>Redraws the list but stays where the player was scrolled (Refresh jumps to the top).</summary>
    private void RefreshKeepingScroll()
    {
        float y = _results.anchoredPosition.y;
        Refresh();
        StartCoroutine(RestoreScroll(y));
    }

    private IEnumerator RestoreScroll(float y)
    {
        yield return null; // after the layout has rebuilt the new rows
        _results.anchoredPosition = new Vector2(_results.anchoredPosition.x, y);
    }

    /// <summary>Into the open window if one takes it; otherwise fly to the item and show it.</summary>
    private void SendOrFocus(ItemEntry item)
    {
        bool sent = item.Token != null && item.Token.IsValid() && SecretHistories.Logic.TokenDestinationSender.SendToOpenSituation(item.Token);
        if (sent)
            _count.text = $"Sent: {item.Title}";
        else
            FocusItem(item);
    }

    /// <summary>A dark wine-red row background: set apart from the normal rows at a glance.</summary>
    private static void Tint(Transform row)
    {
        row.GetComponent<Image>().color = new Color(0.55f, 0.25f, 0.22f, 0.75f);
    }

    /// <summary>
    /// Two independent scroll areas. Top: infected books (contamination chips filter them);
    /// clicking one picks it. Bottom: for the picked book, each contamination's cure (CureIndex)
    /// and the player's skills that work against it, those carrying the cure's principle first
    /// with how much of the needed amount they give; the rest of the 7 comes from the soul,
    /// a memory and the desk's other slots. Picking a book redraws only the bottom area, so the
    /// book list keeps its scroll position.
    /// </summary>
    private void RefreshCures()
    {
        List<ItemEntry> infected = _items.Where(i => i.Book != null && i.Book.Contaminations.Count > 0).ToList();
        List<ItemEntry> books = infected
            .Where(b => _cureFilter.Count == 0 || b.Book.Contaminations.Any(_cureFilter.Contains))
            .Where(b => WorldIndex.TitleMatches(b, _query))
            .OrderBy(b => WorldIndex.LabelOf(b.Book.Contaminations[0]), StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Title)
            .ToList();
        if (_cureBook == null || !books.Contains(_cureBook))
            _cureBook = books.FirstOrDefault();

        _cureBookRows.Clear();
        foreach (ItemEntry book in books)
        {
            ItemEntry captured = book;
            string subtitle = string.Join(", ", book.Book.Contaminations.Select(WorldIndex.LabelOf))
                + (book.Location.Length > 0 ? " · " + book.Location : "");
            Transform row = AddRow(SpriteOf(book.Stack.Element), book.Title, subtitle,
                () => PickCureBook(captured), onDoubleClick: () => SendToOpenWindow(captured));
            foreach (string contamination in book.Book.Contaminations)
            {
                AspectIcon(row, contamination, 24f);
            }
            _cureBookRows[book] = row.GetComponent<Image>();
        }
        HighlightCureBook();
        RefreshCureDetails();
        _count.text = $"{books.Count} of {infected.Count} infected";
    }

    /// <summary>
    /// Souls with a malady (crafting_0_rest.json, "Cure my Malady"): the maladied card (mcho, mhea...)
    /// carries malady.cure.&lt;principle&gt;: N. It goes into one of the player's own beds
    /// (library.bed.rest.*) in the "Restore" slot, with something to drink in "With" (beverage,
    /// covenant or restorative), and the bed's Soul, Skill and Memory slots - each takes only the
    /// bed's own principles - must bring the principle to N (the drink's counts too). Steps: Bed,
    /// the sick soul, Drink, Skill, Soul, Memory; tooltips show what each card gives.
    /// </summary>
    private void RefreshCureSouls()
    {
        List<ItemEntry> sick = _items.Where(i => i.Stack != null && i.Aspects.AspectValue("malady") > 0 && i.Aspects.AspectValue("ability") > 0)
            .GroupBy(i => i.Stack.EntityId).Select(g => g.First()).ToList();
        if (sick.Count == 0)
            AddSectionLabel("No soul has a malady", 0);
        foreach (ItemEntry soul in sick)
        {
            KeyValuePair<string, int> cure = soul.Aspects.FirstOrDefault(a => a.Key.StartsWith("malady.cure.", StringComparison.Ordinal) && a.Value > 0);
            if (cure.Key == null)
                continue;
            string p = cure.Key.Substring("malady.cure.".Length);
            int need = cure.Value;
            string pl = WorldIndex.LabelOf(p);
            Transform row = AddRow(SpriteOf(soul.Stack.Element), soul.Title,
                $"Cure: {pl} {need} in your own bed, with a drink · {soul.Location}", () => FocusItem(soul));
            AspectBadge(row, p, need, 24f, 18f);

            List<DeskEntry> beds = _desks.Where(d => d.IsIdle && d.Situation?.VerbId != null
                    && d.Situation.VerbId.StartsWith("library.bed.rest", StringComparison.Ordinal)
                    && new[] { "ability", "skill", "memory" }.Any(k => SlotCheck.Slot(d, k)?.Required?.ContainsKey(p) == true))
                .Take(16).ToList();
            Func<string, ItemEntry, bool> anyBedTakes = (kind, item) => beds.Any(d => SlotCheck.Slot(d, kind) is SphereSpec slot && SlotCheck.Takes(slot, item));
            Func<ItemEntry, bool> free = i => i.Token != null && i.Token.IsValid() && !SlotCheck.InRunningRecipe(i);
            string Gives(ItemEntry i) => $"{i.Title} · {pl} {i.Aspects.AspectValue(p)}";

            List<ItemEntry> drinks = _items.Where(i => free(i) && (i.Aspects.AspectValue("beverage") > 0 || i.Aspects.AspectValue("restorative") > 0)
                    && i.Aspects.AspectValue("ability") <= 0)
                .GroupBy(i => i.Stack.EntityId).Select(g => g.First())
                .OrderByDescending(i => i.Aspects.AspectValue(p)).ThenBy(i => i.Title).Take(16).ToList();
            List<ItemEntry> skills = _items.Where(i => free(i) && i.Aspects.AspectValue("skill") > 0 && i.Aspects.AspectValue(p) > 0 && anyBedTakes("skill", i))
                .OrderByDescending(i => i.Aspects.AspectValue(p)).Take(16).ToList();
            List<ItemEntry> souls = _items.Where(i => free(i) && i.Aspects.AspectValue("ability") > 0 && i.Aspects.AspectValue("fatigued") <= 0
                    && i.Aspects.AspectValue("malady") <= 0 && i.Aspects.AspectValue(p) > 0 && anyBedTakes("ability", i))
                .GroupBy(i => i.Stack.EntityId).Select(g => g.First())
                .OrderByDescending(i => i.Aspects.AspectValue(p)).Take(16).ToList();
            List<ItemEntry> memories = _items.Where(i => free(i) && i.Aspects.AspectValue("memory") > 0 && i.Aspects.AspectValue(p) > 0 && anyBedTakes("memory", i))
                .GroupBy(i => i.Stack.EntityId).Select(g => g.First())
                .OrderByDescending(i => i.Aspects.AspectValue(p)).Take(16).ToList();

            AddStepStrip(new List<StripGroup> { new StripGroup("Bed", beds.Select(d => Step(DeskArt(d), $"{d.Name} · {d.Location}", () => { OpenPlace(d); _lastOpenedDesk = d; }))) }, 40);
            AddStepStrip(new List<StripGroup> { new StripGroup("Sick", new[] { Step(SpriteOf(soul.Stack.Element), $"{soul.Title} · goes into Restore", () => SendToOpenWindow(soul)) }) }, 40);
            AddStepStrip(new List<StripGroup> { new StripGroup("Drink", drinks.Select(i => Step(SpriteOf(i.Stack.Element), Gives(i), () => SendOrFocus(i)))) }, 40);
            AddStepStrip(new List<StripGroup> { new StripGroup("Skill", skills.Select(i => Step(SpriteOf(i.Stack.Element), Gives(i), () => SendToOpenWindow(i)))) }, 40);
            AddStepStrip(new List<StripGroup> { new StripGroup("Soul", souls.Select(i => Step(SpriteOf(i.Stack.Element), Gives(i), () => SendToOpenWindow(i)))) }, 40);
            AddStepStrip(new List<StripGroup> { new StripGroup("Memory", memories.Select(i => Step(SpriteOf(i.Stack.Element), Gives(i), () => SendToOpenWindow(i)))) }, 40);
        }
        _count.text = $"{sick.Count} soul(s) with a malady";
    }

    /// <summary>
    /// Inks the player can brew (InkIndex: recipes already unlocked, or doable right now with their
    /// own skill and a soul). Each ink is a step list where a single click does the step: a desk
    /// row flies there and opens it, the skill row and a soul row put that card into the open
    /// window. Then the player starts the recipe in the desk.
    /// </summary>
    private void RefreshInks()
    {
        List<InkPlan> plans = InkIndex.Plans(_items, _desks)
            .Where(p => string.IsNullOrEmpty(_query)
                || WorldIndex.LabelOf(p.Recipe.Ink).IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0
                || p.Skill.Title.IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0)
            .ToList();
        Color green = new Color32(0x9C, 0xCB, 0x7A, 0xFF);
        Color red = new Color32(0xD9, 0x79, 0x6A, 0xFF);
        int rows = 0;
        bool brewingLabel = false;
        foreach (InkPlan plan in plans)
        {
            if (rows >= MaxRows)
                break;
            InkRecipe recipe = plan.Recipe;
            if (plan.BrewingAt != null)
            {
                // In progress: set apart at the end, no steps (its skill is busy in that desk).
                if (!brewingLabel)
                {
                    AddSectionLabel("Brewing now", 0);
                    brewingLabel = true;
                }
                Element brewingInk = WorldIndex.ElementOf(recipe.Ink);
                Transform brewingRow = AddRow(SpriteOf(brewingInk), WorldIndex.LabelOf(recipe.Ink),
                    $"<color=#DDA8FF><b>Brewing at {plan.BrewingAt.Name}</b></color> · {plan.Skill.Title}",
                    () => Focus(plan.BrewingAt.Token, plan.BrewingAt.Situation, plan.BrewingAt.Name));
                Tint(brewingRow);
                rows++;
                continue;
            }
            string principle = WorldIndex.LabelOf(recipe.Principle);
            string needs = $"{plan.Skill.Title} + {principle} {recipe.Amount} (the skill gives {plan.SkillGives})";
            string subtitle = plan.Ready ? $"<color=#9CCB7A>Ready</color> · {needs}"
                : recipe.Materials.Count > 0 ? $"<color=#D9796A>Also needs {string.Join(", ", recipe.Materials.Select(WorldIndex.LabelOf))}</color> · {needs}"
                : $"<color=#D9796A>No free desk or soul for it now</color> · {needs}";
            Element ink = WorldIndex.ElementOf(recipe.Ink);
            Transform header = AddRow(SpriteOf(ink), WorldIndex.LabelOf(recipe.Ink), subtitle,
                () => Watchman.Get<AureateNotifier>().ShowAspectDetails(ink, SpriteOf(ink)));
            if (ink?.Aspects != null)
            {
                foreach (KeyValuePair<string, int> aspect in ink.Aspects.Where(a => a.Value > 0 && WorldIndex.IsPrinciple(a.Key))
                             .OrderByDescending(a => a.Value))
                    AspectBadge(header, aspect.Key, aspect.Value, 22f, 16f);
            }
            rows++;
            if (!plan.Ready)
                continue;

            // Souls of the desk opened from the row (else the first): each desk's soul slot takes its own.
            DeskEntry inkDesk = plan.Desks.Contains(_lastOpenedDesk) ? _lastOpenedDesk : plan.Desks[0];
            var groups = new List<StripGroup>
            {
                new StripGroup("Desk", plan.Desks.Take(3).Select(d => Step(DeskArt(d), $"{d.Name} · {d.Location}",
                    () => { OpenPlace(d); _lastOpenedDesk = d; RefreshKeepingScroll(); }))),
                new StripGroup("Skill", new[] { Step(SpriteOf(plan.Skill.Stack.Element), $"{plan.Skill.Title} · {principle} {plan.SkillGives}", () => SendToOpenWindow(plan.Skill)) }),
                new StripGroup("Soul", plan.SoulsByDesk[inkDesk].Take(4).Select(s => Step(SpriteOf(s.Stack.Element),
                    $"{s.Title}{(s.Stack.Quantity > 1 ? " ×" + s.Stack.Quantity : "")} · {principle} {s.Aspects.AspectValue(recipe.Principle)}", () => SendToOpenWindow(s)))),
            };
            AddStepStrip(groups, 40);
            rows++;
        }
        if (plans.Count == 0)
            AddSectionLabel("No ink you can brew now", 0);
        _count.text = $"{plans.Count(p => p.Ready)} ready · {plans.Count(p => p.BrewingAt != null)} brewing";
    }

    private void PickCureBook(ItemEntry book)
    {
        _cureBook = book;
        HighlightCureBook();
        RefreshCureDetails();
        ShowDetails(book.Stack, book.Title);
    }

    private void HighlightCureBook()
    {
        foreach (KeyValuePair<ItemEntry, Image> pair in _cureBookRows)
        {
            if (pair.Value != null)
                pair.Value.color = pair.Key == _cureBook ? new Color(1f, 0.82f, 0.5f, 0.85f) : new Color(1f, 1f, 1f, 0.45f);
        }
    }

    private void RefreshCureDetails()
    {
        for (int i = _cureContent.childCount - 1; i >= 0; i--)
            Destroy(_cureContent.GetChild(i).gameObject);
        _cureScroll.verticalNormalizedPosition = 1f;
        if (_cureBook == null)
        {
            AddSectionLabel("No infected books", 0, _cureContent);
            return;
        }
        Color green = new Color32(0x9C, 0xCB, 0x7A, 0xFF);
        Color red = new Color32(0xD9, 0x79, 0x6A, 0xFF);
        ItemEntry book = _cureBook;
        foreach (string contamination in book.Book.Contaminations)
        {
            string label = WorldIndex.LabelOf(contamination);
            Cure cure = CureIndex.For(contamination);
            if (cure == null)
            {
                AddRow(SpriteOf(contamination), $"{label}: {book.Title}", "No cure recipe known",
                    () => { }, parent: _cureContent);
                continue;
            }
            string p = cure.Principle;
            string pl = WorldIndex.LabelOf(p);
            int need = cure.Amount;

            // A desk (library.*) that takes the book, with a skill and a soul slot.
            List<DeskEntry> desks = _desks.Where(d => d.IsIdle && d.Situation?.VerbId != null
                    && d.Situation.VerbId.StartsWith("library.", StringComparison.Ordinal)
                    && SlotCheck.Slot(d, "skill") != null && SlotCheck.Slot(d, "ability") != null
                    && SlotCheck.OtherSlotTakes(d, book))
                .Take(16).ToList();
            Func<string, ItemEntry, bool> anyDeskTakes = (kind, item) =>
                desks.Any(d => SlotCheck.Slot(d, kind) is SphereSpec slot && SlotCheck.Takes(slot, item));
            Func<ItemEntry, bool> free = i => i.Token != null && i.Token.IsValid() && !SlotCheck.InRunningRecipe(i);
            List<ItemEntry> skills = CureIndex.SkillsFor(cure, _items).Where(i => free(i) && anyDeskTakes("skill", i))
                .OrderByDescending(i => i.Aspects.AspectValue(p)).ThenBy(i => i.Title).ToList();
            List<ItemEntry> souls = _items.Where(i => free(i) && i.Aspects.AspectValue("ability") > 0 && i.Aspects.AspectValue("fatigued") <= 0
                    && i.Aspects.AspectValue("malady") <= 0 && i.Aspects.AspectValue(p) > 0 && anyDeskTakes("ability", i))
                .GroupBy(i => i.Stack.EntityId).Select(g => g.First())
                .OrderByDescending(i => i.Aspects.AspectValue(p)).Take(16).ToList();
            List<ItemEntry> memories = _items.Where(i => free(i) && i.Aspects.AspectValue("memory") > 0 && i.Aspects.AspectValue(p) > 0
                    && anyDeskTakes("memory", i))
                .GroupBy(i => i.Stack.EntityId).Select(g => g.First())
                .OrderByDescending(i => i.Aspects.AspectValue(p)).Take(16).ToList();

            // "With" (i2 on the desks): an ink or a tool, not fatigued; its principles count too.
            List<ItemEntry> withItems = _items.Where(i => free(i) && (i.Aspects.AspectValue("ink") > 0 || i.Aspects.AspectValue("tool") > 0)
                    && i.Aspects.AspectValue("fatigued") <= 0 && i.Aspects.AspectValue(p) > 0
                    && desks.Any(d => WithSlot(d) is SphereSpec slot && SlotCheck.Takes(slot, i)))
                .GroupBy(i => i.Stack.EntityId).Select(g => g.First())
                .OrderByDescending(i => i.Aspects.AspectValue(p)).Take(16).ToList();

            // The best each slot can bring (the book's own principle counts too).
            int bookGives = book.Aspects.AspectValue(p);
            int bestWith = withItems.Select(i => i.Aspects.AspectValue(p)).DefaultIfEmpty(0).Max();
            int bestSkill = skills.Select(i => i.Aspects.AspectValue(p)).DefaultIfEmpty(0).Max();
            int bestSoul = souls.Select(i => i.Aspects.AspectValue(p)).DefaultIfEmpty(0).Max();
            int bestMemory = memories.Select(i => i.Aspects.AspectValue(p)).DefaultIfEmpty(0).Max();
            int best = bookGives + bestSkill + bestSoul + bestMemory + bestWith;
            string parts = $"skill {bestSkill} + soul {bestSoul} + memory {bestMemory} + ink/tool {bestWith}" + (bookGives > 0 ? $" + book {bookGives}" : "");
            string verdict = skills.Count == 0 ? $"<color=#D9796A>No skill of yours works against it: needs {WorldIndex.LabelOf(cure.Effective)}</color>"
                : desks.Count == 0 ? "<color=#D9796A>No free desk takes this book</color>"
                : best >= need ? $"<color=#9CCB7A>You can reach {pl} {need}: up to {best} ({parts})</color>"
                : $"<color=#D9796A>At most {pl} {best} of {need}: {need - best} short ({parts})</color>";
            Transform header = AddRow(SpriteOf(contamination), $"{label}: {book.Title}",
                $"Any desk: a skill {WorldIndex.LabelOf(cure.Effective)} + {pl} {need} in total",
                () => Watchman.Get<AureateNotifier>().ShowAspectDetails(WorldIndex.ElementOf(contamination), SpriteOf(contamination)),
                parent: _cureContent);
            AspectBadge(header, p, need, 28f, 20f);
            AddSectionLabel(verdict, 20, _cureContent);
            if (skills.Count == 0 || desks.Count == 0)
                continue;
            string Gives(ItemEntry i) => $"{i.Title} · {pl} {i.Aspects.AspectValue(p)} of {need}";
            AddCureStrip("Desk", desks.Select(d => Step(DeskArt(d), $"{d.Name} · {d.Location}", () => { OpenPlace(d); _lastOpenedDesk = d; })));
            AddCureStrip("Book", new[] { Step(SpriteOf(book.Stack.Element), book.Title, () => SendOrFocus(book)) });
            AddCureStrip("Skill", skills.Take(16).Select(i => Step(SpriteOf(i.Stack.Element), Gives(i), () => SendToOpenWindow(i))));
            AddCureStrip("Soul", souls.Select(i => Step(SpriteOf(i.Stack.Element), Gives(i), () => SendToOpenWindow(i))));
            AddCureStrip("Memory", memories.Select(i => Step(SpriteOf(i.Stack.Element), Gives(i), () => SendToOpenWindow(i))));
            AddCureStrip("With", withItems.Select(i => Step(SpriteOf(i.Stack.Element), Gives(i) + (i.Aspects.AspectValue("ink") > 0 ? " (ink: used up)" : ""), () => SendOrFocus(i))));
        }
    }

    /// <summary>The desk's "With" slot (i2): takes an ink or a tool.</summary>
    private static SphereSpec WithSlot(DeskEntry desk) =>
        desk.Situation?.Verb?.Slots?.FirstOrDefault(s => s.Required != null && (s.Required.ContainsKey("ink") || s.Required.ContainsKey("tool")));

    /// <summary>One step row in the cure details area (the second scroll area, not the results).</summary>
    private void AddCureStrip(string label, IEnumerable<StripStep> steps) =>
        AddStepStrip(new List<StripGroup> { new StripGroup(label, steps) }, 20, _cureContent);

    /// <summary>Small gold caption between result rows, e.g. "Your skills that can cure it".</summary>
    private void AddSectionLabel(string text, int indent, Transform parent = null)
    {
        TextMeshProUGUI label = GameStyle.Text(parent ?? _results, "Section", text, 14f, GameStyle.Gold, FontStyles.Bold | FontStyles.SmallCaps);
        label.margin = new Vector4(indent + 8, 4, 0, 0);
        GameStyle.Size(label, height: 24f);
    }

    /// <summary>
    /// The workstation running a recipe with this item inside (in its slots or storage), or null.
    /// A book being read sits in the desk's SituationStorage until the recipe completes.
    /// </summary>
    private static Situation BusyHost(ItemEntry item)
    {
        if (item.HostToken == null || !item.HostToken.IsValid() || !(item.HostToken.Payload is Situation host))
            return null;
        try
        {
            SecretHistories.Enums.StateEnum state = host.StateIdentifier;
            bool working = state != SecretHistories.Enums.StateEnum.Unstarted
                && state != SecretHistories.Enums.StateEnum.Inchoate
                && state != SecretHistories.Enums.StateEnum.Complete;
            return working ? host : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>"at Desk: Eva". No time left: it would only change on a redraw, so it went stale.</summary>
    private static string BusyText(ItemEntry item)
    {
        return BusyHost(item) == null ? "" : "at " + (item.HostLabel ?? "a desk");
    }

    /// <summary>Fades a result row (background and contents), e.g. for something already in progress.</summary>
    private static void Fade(Transform row)
    {
        row.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.2f);
        row.gameObject.AddComponent<CanvasGroup>().alpha = 0.55f;
    }

    /// <summary>
    /// People tab in Invite mode: each visitor whose address is in the Writing Case, with what the
    /// letter needs and click-through steps (InviteIndex): open a desk, then Writing Case, the
    /// cheapest fitting ink and a skill go into it with a single click each; start the letter in
    /// the desk; then the Post Office row flies there to post it. Visitors already here, written to
    /// or on their way say so instead.
    /// </summary>
    /// <summary>Invite mode: the guest whose recipe the lower area shows.</summary>
    private string _invitePick;
    private readonly Dictionary<string, Image> _inviteRows = new Dictionary<string, Image>();

    /// <summary>
    /// Two areas, as in Cures: the guests above (scrolling on its own), the picked guest's recipe
    /// below - Desk, Case, Ink, Skill and Soul rows. Picking redraws only the lower area.
    /// </summary>
    private void RefreshInvites()
    {
        List<InvitePlan> plans = InviteIndex.Plans(_people, _items, _desks)
            .Where(p => _peopleFilter.Matches(p.Person, _query))
            .ToList();
        List<InvitePlan> open = plans.Where(p => p.Status == null || !IsInvited(p)).ToList();
        List<InvitePlan> done = plans.Where(p => p.Status != null && IsInvited(p)).ToList();
        // Echidna is summoned on the Moor, not written to: her own row under "Summon".
        bool echidnaShown = (string.IsNullOrEmpty(_query) || "Echidna Mrs E".IndexOf(_query, StringComparison.OrdinalIgnoreCase) >= 0)
                            && (!_peopleFilter.Kind.HasValue || _peopleFilter.Kind.Value == PersonKind.Visitor)
                            && !_peopleFilter.NumaOnly && _peopleFilter.Principles.All(pr => pr == "scale");
        SummonPlan echidna = echidnaShown ? SummonIndex.Echidna(_people, _items, _desks) : null;
        bool echidnaOpen = echidna != null && echidna.Status != "Here now" && echidna.Status != "On the way";
        if (_invitePick == null || !open.Any(p => p.Person.PersonId == _invitePick) && !(_invitePick == EchidnaKey && echidnaOpen))
            _invitePick = open.FirstOrDefault(p => p.Ready)?.Person.PersonId ?? open.FirstOrDefault()?.Person.PersonId
                ?? (echidnaOpen ? EchidnaKey : null);
        _inviteRows.Clear();
        foreach (InvitePlan plan in open)
        {
            InvitePlan captured = plan;
            PersonEntry person = plan.Person;
            string needs = plan.Principle != null ? $"Letter: {WorldIndex.LabelOf(plan.Principle)} {plan.Amount}" : "Letter";
            string subtitle = plan.Ready ? $"<color=#9CCB7A>Ready to write</color> · {needs}"
                : $"<color=#D9796A>{plan.Status ?? "No free desk"}</color> · {needs}";
            Transform header = AddRow(SpriteOf(person.Element), person.Name, subtitle, () => PickInvite(captured),
                onDoubleClick: () => ShowDetails(person.Stack, person.Name));
            if (plan.Principle != null)
                AspectBadge(header, plan.Principle, plan.Amount, 22f, 16f);
            AddSummonNowClick(header, plan);
            _inviteRows[person.PersonId] = header.GetComponent<Image>();
        }
        if (echidnaOpen)
        {
            AddSectionLabel("Summon (no letter)", 0);
            SummonPlan captured = echidna;
            string subtitle = echidna.Ready ? "<color=#9CCB7A>Ready: A Walk on the Moor</color> · Scale 7 + Serpent-Milk"
                : $"<color=#D9796A>{echidna.Status ?? (echidna.Milk == null ? "No Serpent-Milk" : echidna.Soul == null ? "No soul reaches Scale 7" : "Not ready")}</color> · Scale 7 + Serpent-Milk";
            Transform header = AddRow(SpriteOf("echidna"), "Echidna", subtitle, () => PickEchidna(captured));
            AspectBadge(header, "scale", SummonIndex.ScaleNeeded, 22f, 16f);
            _inviteRows[EchidnaKey] = header.GetComponent<Image>();
        }
        if (echidna != null && !echidnaOpen)
            done.Add(null); // counted below; drawn as Echidna's own row
        if (done.Count > 0)
        {
            AddSectionLabel($"Already invited or here ({done.Count})", 0);
            if (echidna != null && !echidnaOpen)
            {
                SummonPlan captured = echidna; // still clickable: the recipe shows below, with her status
                Transform row = AddRow(SpriteOf("echidna"), "Echidna", $"<color=#DDA8FF><b>{echidna.Status}</b></color>", () => PickEchidna(captured));
                Tint(row);
            }
            done.RemoveAll(p => p == null);
            foreach (InvitePlan plan in done)
            {
                PersonEntry person = plan.Person;
                Transform row = AddRow(SpriteOf(person.Element), person.Name, $"<color=#DDA8FF><b>{plan.Status}</b></color>",
                    () => ShowDetails(person.Stack, person.Name));
                Tint(row);
                if (plan.Status.StartsWith("Invitation written", StringComparison.Ordinal))
                    AddSummonNowClick(row, plan); // written but not posted: the cheat posts it
            }
        }
        if (plans.Count == 0)
            AddSectionLabel("No noted addresses: note a visitor's calling card in the Writing Case", 0);
        HighlightInvite();
        if (_invitePick == EchidnaKey && echidnaOpen)
            ShowEchidnaRecipe(echidna);
        else
            ShowInviteRecipe(open.FirstOrDefault(p => p.Person.PersonId == _invitePick));
        _count.text = $"{plans.Count(p => p.Ready)} ready · {done.Count} invited";
    }

    private void PickInvite(InvitePlan plan)
    {
        _invitePick = plan.Person.PersonId;
        HighlightInvite();
        ShowInviteRecipe(plan);
    }

    private const string EchidnaKey = "summon:echidna";

    private void PickEchidna(SummonPlan plan)
    {
        _invitePick = EchidnaKey;
        HighlightInvite();
        ShowEchidnaRecipe(plan);
    }

    /// <summary>
    /// Echidna's summoning, like a letter's recipe: Moor › Soul › Skill › Serpent-Milk (the milk's
    /// slot opens once soul and skill are in). Middle click on a missing Serpent-Milk gives one (cheat).
    /// </summary>
    private void ShowEchidnaRecipe(SummonPlan plan)
    {
        for (int i = _cureContent.childCount - 1; i >= 0; i--)
            Destroy(_cureContent.GetChild(i).gameObject);
        _cureScroll.verticalNormalizedPosition = 1f;
        Element milkElement = WorldIndex.ElementOf("serpentmilk");
        AddRow(SpriteOf("echidna"), "Summon Echidna, Mother of Monsters",
            plan.Status != null ? $"<color=#D9796A>{plan.Status}</color>"
                : "A Walk on the Moor: soul + skill with Echidna's Prowlings + Serpent-Milk, Scale 7 in total",
            () => { }, parent: _cureContent);
        if (plan.Moor == null)
            return;
        var steps = new List<StripGroup>
        {
            new StripGroup("Place", new[] { Step(DeskArt(plan.Moor), $"{plan.Moor.Name} · {plan.Moor.Location}", () => { OpenPlace(plan.Moor); _lastOpenedDesk = plan.Moor; }) }),
        };
        var put = new List<StripStep>();
        if (plan.Soul != null)
            put.Add(Step(SpriteOf(plan.Soul.Stack.Element), $"{plan.Soul.Title} · Scale {plan.Soul.Aspects.AspectValue("scale")}", () => SendToOpenWindow(plan.Soul)));
        put.Add(Step(SpriteOf(plan.Skill.Stack.Element), $"{plan.Skill.Title} · Scale {plan.Skill.Aspects.AspectValue("scale")} · knows Echidna's Prowlings",
            () => SendToOpenWindow(plan.Skill)));
        if (plan.Milk != null)
            put.Add(Step(SpriteOf(plan.Milk.Stack.Element), $"{plan.Milk.Title} · Scale 3 · goes into the Serpent-Milk slot, used up", () => SendToOpenWindow(plan.Milk)));
        else if (milkElement != null)
            put.Add(new StripStep
            {
                Icon = SpriteOf(milkElement), Tint = new Color(1f, 0.45f, 0.4f, 0.6f),
                Tooltip = "No Serpent-Milk: craft it (Serpents & Venoms + Perinculate, Scale 15) · middle click: get it",
                Click = () => Watchman.Get<AureateNotifier>().ShowAspectDetails(milkElement, SpriteOf(milkElement)),
                Middle = () => GiveToPortage("serpentmilk", milkElement.Label),
            });
        steps.Add(new StripGroup("Put in", put));
        AddStepStrip(steps, 20, _cureContent);
        AddSectionLabel($"Scale {plan.Scale} of {SummonIndex.ScaleNeeded} (skill + soul + milk 3) · 60 s · the soul tires", 20, _cureContent);
    }

    private void HighlightInvite()
    {
        foreach (KeyValuePair<string, Image> pair in _inviteRows)
        {
            if (pair.Value != null)
                pair.Value.color = pair.Key == _invitePick ? new Color(1f, 0.82f, 0.5f, 0.85f) : new Color(1f, 1f, 1f, 0.45f);
        }
    }

    /// <summary>The picked guest's letter, one step per row, into the lower area.</summary>
    private void ShowInviteRecipe(InvitePlan plan)
    {
        for (int i = _cureContent.childCount - 1; i >= 0; i--)
            Destroy(_cureContent.GetChild(i).gameObject);
        _cureScroll.verticalNormalizedPosition = 1f;
        if (plan == null)
        {
            AddSectionLabel("Pick a guest above", 0, _cureContent);
            return;
        }
        PersonEntry person = plan.Person;
        string needs = plan.Principle != null ? $"{WorldIndex.LabelOf(plan.Principle)} {plan.Amount} in total at the desk" : "";
        Transform header = AddRow(SpriteOf(person.Element), $"Invite {person.Name}",
            plan.Ready ? $"Writing Case + ink + skill (+ soul): {needs}" : $"<color=#D9796A>{plan.Status ?? "No free desk"}</color>",
            () => ShowDetails(person.Stack, person.Name), parent: _cureContent);
        AddInviteResult(header, plan);
        if (!plan.Ready)
            return;
        int inkGives = plan.Ink.Aspects.AspectValue(plan.Principle);
        string p = WorldIndex.LabelOf(plan.Principle);
        // Each desk has its own checked skill and soul: the desk opened from the row (else the best).
        InviteOption chosen = plan.Options.FirstOrDefault(o => o.Desk == _lastOpenedDesk) ?? plan.Options[0];
        AddStepStrip(new List<StripGroup> { new StripGroup("Desk", plan.Options.Take(16).Select(o => Step(DeskArt(o.Desk),
            $"{o.Desk.Name} · {o.Desk.Location} · {o.Skill.Title}" + (o.Soul != null ? $" + {o.Soul.Title}" : " alone"),
            () => { OpenPlace(o.Desk); _lastOpenedDesk = o.Desk; ShowInviteRecipe(plan); }))) }, 20, _cureContent);
        var put = new List<StripStep>
        {
            Step(SpriteOf(plan.WritingCase.Stack.Element), plan.WritingCase.Title, () => SendToOpenWindow(plan.WritingCase)),
            Step(SpriteOf(plan.Ink.Stack.Element), $"{plan.Ink.Title} · {p} {inkGives}" + (plan.Ink.Aspects.AspectValue("liquid") > 0 ? " (used up)" : " (stays)"), () => SendToOpenWindow(plan.Ink)),
            Step(SpriteOf(chosen.Skill.Stack.Element), $"{chosen.Skill.Title} · {p} {chosen.Skill.Aspects.AspectValue(plan.Principle)}", () => SendToOpenWindow(chosen.Skill)),
        };
        if (chosen.Soul != null)
            put.Add(Step(SpriteOf(chosen.Soul.Stack.Element), $"{chosen.Soul.Title} · {p} {chosen.Soul.Aspects.AspectValue(plan.Principle)} · this desk's soul slot takes it",
                () => SendToOpenWindow(chosen.Soul)));
        AddStepStrip(new List<StripGroup> { new StripGroup("Put in", put) }, 20, _cureContent);
        AddSectionLabel($"For {chosen.Desk.Name}: {p} " + (inkGives + chosen.Skill.Aspects.AspectValue(plan.Principle) + (chosen.Soul?.Aspects.AspectValue(plan.Principle) ?? 0))
                        + $" of {plan.Amount} · click another desk to see its own cards", 20, _cureContent);
    }

    /// <summary>
    /// At the right of the recipe's header (where the principle badge was; the subtitle already says
    /// how much is needed): the card the recipe writes, the visitor's invitation
    /// (letter.summon.&lt;visitor&gt;). Click shows its details; middle click puts one into a free slot
    /// of the carried things (cheat), like the Cookbook's results.
    /// </summary>
    private void AddInviteResult(Transform header, InvitePlan plan)
    {
        string id = "letter.summon." + plan.Person.PersonId;
        Element element = WorldIndex.ElementOf(id);
        if (element == null)
            return;
        Image icon = GameStyle.Panel(header, "Result", "", Color.white);
        icon.sprite = SpriteOf(element);
        icon.preserveAspect = true;
        GameStyle.Size(icon, 40f, 40f);
        Button button = icon.gameObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(1.4f, 1.3f, 1f, 1f);
        button.colors = colors;
        button.onClick.AddListener(() => Watchman.Get<AureateNotifier>().ShowAspectDetails(element, SpriteOf(element)));
        icon.gameObject.AddComponent<MiddleClick>().Click = () => GiveToPortage(id, element.Label);
        var hover = icon.gameObject.AddComponent<HoverHandler>();
        string before = null;
        hover.Enter = () => { before = _count.text; _count.text = $"{element.Label} · click: details · middle click: get it"; };
        hover.Exit = () => { if (before != null) _count.text = before; };
    }

    /// <summary>One clickable icon in a step strip; the tooltip shows in the header while hovered.</summary>
    private sealed class StripStep
    {
        public Sprite Icon;
        /// <summary>Icon colour; white unless the step is missing (dimmed red).</summary>
        public Color Tint = Color.white;
        /// <summary>Optional middle-click action (the Cookbook's cheat: give the card).</summary>
        public Action Middle;
        public string Tooltip;
        public Action Click;
    }

    private sealed class StripGroup
    {
        public string Label;
        public List<StripStep> Steps;
        public StripGroup(string label, IEnumerable<StripStep> steps)
        {
            Label = label;
            Steps = steps.ToList();
        }
    }

    private static StripStep Step(Sprite icon, string tooltip, Action click) => new StripStep { Icon = icon, Tooltip = tooltip, Click = click };

    private static Sprite DeskArt(DeskEntry desk)
    {
        try
        {
            return desk.Situation.GetDetailSprite();
        }
        catch (Exception)
        {
            return null; // some verbs have no large icon
        }
    }

    /// <summary>
    /// Steps as one compact row of icons, left to right: "Desk [icons] › Case [icon] › Ink …".
    /// A click does the step (open the desk, put the card in); hovering shows the name in the
    /// window header, so the row needs no text per icon.
    /// </summary>
    private void AddStepStrip(List<StripGroup> groups, int indent, Transform parent = null)
    {
        Image row = GameStyle.Panel(parent ?? _results, "Steps", "slots_bg", new Color(1f, 1f, 1f, 0.2f));
        GameStyle.Size(row, height: 42f);
        GameStyle.Layout<HorizontalLayoutGroup>(row, 5f, new RectOffset(8 + indent, 8, 4, 4), TextAnchor.MiddleLeft);
        bool first = true;
        foreach (StripGroup group in groups.Where(g => g.Steps.Count > 0))
        {
            if (!first)
                GameStyle.Size(GameStyle.Text(row.transform, "Sep", "›", 16f, GameStyle.GoldFaint, FontStyles.Bold, TextAlignmentOptions.Center), 10f, 32f);
            first = false;
            if (!string.IsNullOrEmpty(group.Label)) // no label: the icons speak for themselves
                GameStyle.Size(GameStyle.Text(row.transform, "Label", group.Label, 12f, GameStyle.Gold, FontStyles.SmallCaps, TextAlignmentOptions.MidlineRight), 30f, 32f);
            foreach (StripStep step in group.Steps)
            {
                Image icon = GameStyle.Panel(row.transform, "Step", "", step.Tint);
                icon.sprite = step.Icon;
                icon.preserveAspect = true;
                GameStyle.Size(icon, 32f, 32f);
                Button button = icon.gameObject.AddComponent<Button>();
                ColorBlock colors = button.colors;
                colors.highlightedColor = new Color(1.4f, 1.3f, 1f, 1f);
                colors.pressedColor = new Color(1.8f, 1.6f, 1.1f, 1f);
                button.colors = colors;
                StripStep captured = step;
                button.onClick.AddListener(() => captured.Click());
                if (captured.Middle != null)
                    icon.gameObject.AddComponent<MiddleClick>().Click = captured.Middle;
                var hover = icon.gameObject.AddComponent<HoverHandler>();
                string before = null;
                hover.Enter = () => { before = _count.text; _count.text = captured.Tooltip; };
                hover.Exit = () => { if (before != null) _count.text = before; };
            }
        }
    }

    /// <summary>Middle click on the guest's portrait: <see cref="SummonNow"/>.</summary>
    private void AddSummonNowClick(Transform row, InvitePlan plan)
    {
        if (row.Find("Art")?.GetComponent<Image>() is Image art)
        {
            art.raycastTarget = true;
            art.gameObject.AddComponent<MiddleClick>().Click = () => SummonNow(plan);
        }
    }

    /// <summary>
    /// Cheat (middle click on a portrait in Invite): does what posting the invitation does.
    /// postoffice.invitation.send spawns, as an additional recipe, a situation of verb
    /// visitor.&lt;id&gt; at ~/arrivalverbs running postoffice.invitation.send.&lt;id&gt; ("Zachary
    /// Incoming?", 60 s); its greedy slot pulls the visitor from ~/visitors.offstage and he arrives
    /// when it ends. The same situation is created here directly, as CollectionTrigger does;
    /// TryActivateRecipeCommand doesn't check reqs, so no letter is needed. A letter already
    /// written for this guest is removed, as posting would use it up.
    /// </summary>
    private void SummonNow(InvitePlan plan)
    {
        string id = plan.Person.PersonId;
        string recipeId = "postoffice.invitation.send." + id;
        Recipe recipe = Watchman.Get<Compendium>().GetEntityById<Recipe>(recipeId);
        if (recipe == null || !recipe.IsValid())
        {
            _count.text = $"No arrival recipe for {plan.Person.Name}";
            return;
        }
        HornedAxe axe = Watchman.Get<HornedAxe>();
        if (axe.GetRegisteredSituations().Any(s => s?.VerbId == recipe.ActionId))
        {
            _count.text = $"{plan.Person.Name} is already on the way";
            return;
        }
        SecretHistories.Spheres.Sphere arrivals = axe.GetSphereByAbsolutePath(new SecretHistories.Fucine.FucinePath("~/arrivalverbs"));
        if (arrivals == null || !arrivals.IsValid())
        {
            _count.text = "No arrivals area found";
            return;
        }
        var situation = new SecretHistories.Commands.SituationCreationCommand(recipe.ActionId).WithRecipeAboutToActivate(recipe.Id);
        new SecretHistories.Commands.TokenCreationCommand(situation, new SecretHistories.UI.TokenLocation(Vector3.zero, arrivals))
            .Execute(new Context(Context.ActionSource.JustSpawned), arrivals);
        foreach (ItemEntry letter in _items.Where(i => i.Stack?.EntityId == "letter.summon." + id && i.Token != null && i.Token.IsValid()).ToList())
            letter.Token.Retire(SecretHistories.Enums.RetirementVFX.None);
        _log.LogInfo($"Summoned {id}: {recipe.Id} at ~/arrivalverbs");
        Rescan(); // the guest moves to "on the way"
        _count.text = $"{plan.Person.Name}: invitation posted";
    }

    private static bool IsInvited(InvitePlan plan) =>
        plan.Status.StartsWith("Here", StringComparison.Ordinal) || plan.Status.StartsWith("Invitation", StringComparison.Ordinal);

    /// <summary>
    /// The Sweet Bones' helper deck as numbered cards, next to come on the left (SweetBonesDeck).
    /// Drag a card to another place to change who comes when. Hover names the card in the header.
    /// All faces are shown: the player has met the whole deck (a face-down version was dropped).
    /// </summary>
    private void AddSweetBonesDeck()
    {
        for (int c = _deckArea.childCount - 1; c >= 0; c--)
            Destroy(_deckArea.GetChild(c).gameObject);
        List<Token> cards = SweetBonesDeck.Cards();
        List<string> drawn = SweetBonesDeck.Drawn();
        // Only on the Assistants filter: the deck is about hiring helpers, noise elsewhere.
        bool show = (cards.Count > 0 || drawn.Count > 0) && !_inviteMode
            && _peopleFilter.Kind == PersonKind.Assistant;
        _deckArea.gameObject.SetActive(show);
        if (!show)
            return;
        AddSectionLabel("Sweet Bones · Seek Unusual Help: who comes next (drag to reorder)", 0, _deckArea);
        Image row = GameStyle.Panel(_deckArea, "Deck", "slots_bg", new Color(1f, 1f, 1f, 0.25f));
        GameStyle.Size(row, height: 92f);
        GameStyle.Layout<HorizontalLayoutGroup>(row, 6f, new RectOffset(10, 10, 6, 6), TextAnchor.MiddleLeft);
        if (cards.Count == 0)
            GameStyle.Text(row.transform, "Empty", "Empty: the next draw reshuffles the whole deck", 14f, GameStyle.Cream, FontStyles.Italic, TextAlignmentOptions.Left);
        for (int i = 0; i < cards.Count; i++)
        {
            Token card = cards[i];
            string id = card.PayloadEntityId;
            Element element = WorldIndex.ElementOf(id);
            RectTransform slot = GameStyle.Rect("Card", row.transform);
            GameStyle.Size(slot, 58f, 80f);
            Image face = GameStyle.Panel(slot, "Face", "", Color.white);
            face.sprite = SpriteOf(element);
            face.preserveAspect = true;
            GameStyle.Stretch(face.rectTransform);
            Image badge = GameStyle.Panel(slot, "Badge", "box-rnd-16px", new Color(0f, 0f, 0f, 0.8f));
            badge.rectTransform.anchorMin = badge.rectTransform.anchorMax = new Vector2(0f, 1f);
            badge.rectTransform.pivot = new Vector2(0f, 1f);
            badge.rectTransform.sizeDelta = new Vector2(20f, 20f);
            badge.raycastTarget = false;
            TextMeshProUGUI number = GameStyle.Text(badge.transform, "N", (i + 1).ToString(), 14f, GameStyle.Cream, FontStyles.Bold, TextAlignmentOptions.Center);
            GameStyle.Stretch(number.rectTransform);
            number.raycastTarget = false;

            string name = element?.Label ?? id;
            var hover = slot.gameObject.AddComponent<HoverHandler>();
            string before = null;
            hover.Enter = () => { before = _count.text; _count.text = $"{name}"; };
            hover.Exit = () => { if (before != null) _count.text = before; };
            var drag = slot.gameObject.AddComponent<DeckCardDrag>();
            drag.Index = i;
            drag.Row = (RectTransform)row.transform;
            drag.Dropped = (from, to) =>
            {
                SweetBonesDeck.Move(from, to);
                AddSweetBonesDeck(); // only the deck redraws; the list below stays as it is
            };
            drag.Clicked = () => ShowDetails(card.Payload, name);
        }
        if (drawn.Count > 0)
            AddDrawnHelpers(drawn, (RectTransform)row.transform);
    }

    /// <summary>Helpers already dealt this round: small faded faces, they return at the reshuffle.</summary>
    private void AddDrawnHelpers(List<string> drawn, RectTransform deckRow)
    {
        AddSectionLabel("Already drawn (drag one up to put it back; all return at the reshuffle)", 0, _deckArea);
        Image row = GameStyle.Panel(_deckArea, "Drawn", "slots_bg", new Color(1f, 1f, 1f, 0.12f));
        GameStyle.Size(row, height: 62f);
        GameStyle.Layout<HorizontalLayoutGroup>(row, 6f, new RectOffset(10, 10, 5, 5), TextAnchor.MiddleLeft);
        foreach (string id in drawn)
        {
            Element element = WorldIndex.ElementOf(id);
            RectTransform slot = GameStyle.Rect("Card", row.transform);
            GameStyle.Size(slot, 38f, 52f);
            Image face = GameStyle.Panel(slot, "Face", "", new Color(1f, 1f, 1f, 0.55f));
            face.sprite = SpriteOf(element);
            face.preserveAspect = true;
            GameStyle.Stretch(face.rectTransform);
            string name = element?.Label ?? id;
            var hover = slot.gameObject.AddComponent<HoverHandler>();
            string before = null;
            hover.Enter = () => { before = _count.text; _count.text = name; };
            hover.Exit = () => { if (before != null) _count.text = before; };
            var drag = slot.gameObject.AddComponent<DeckCardDrag>();
            drag.Row = (RectTransform)row.transform;
            drag.Target = deckRow;
            drag.Inserted = to =>
            {
                SweetBonesDeck.Insert(id, to);
                AddSweetBonesDeck();
            };
            drag.Dropped = (_, _) => AddSweetBonesDeck(); // dropped elsewhere: back to its place
            drag.Clicked = () =>
                // No card exists for a dealt helper: show the element itself.
                Watchman.Get<AureateNotifier>().ShowAspectDetails(element, ResourcesManager.GetAppropriateSpriteForElement(element));
        }
    }

    private void AddPersonRow(PersonEntry person)
    {
        string where = person.Here ? "Here" + (person.Location.Length > 0 ? ": " + person.Location : "") : "Away";
        if (person.Kind == PersonKind.Assistant)
            where = "Assistant · " + (person.FromDeck ? "Sweet Bones deck (Seek Unusual Help)" : where);
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
        }, onDoubleClick: () => SendToOpenWindow(person.Here ? person.Token : null, person.Name));
        IEnumerable<KeyValuePair<string, int>> principles = person.Principles
            .OrderByDescending(p => _peopleFilter.Principles.Contains(p.Key))
            .ThenByDescending(p => p.Value)
            .Take(5);
        foreach (KeyValuePair<string, int> principle in principles)
            AspectBadge(row, principle.Key, principle.Value, 22f, 16f);
    }

    /// <summary>
    /// With Kind = Souls: one chip per soul, picked one at a time (each row then carries a verdict
    /// for that soul). An "Evolve via" row of Wisdoms used to sit above it; with the verdicts it
    /// told nothing new and was dropped. Hidden otherwise, and the pick cleared so it doesn't linger.
    /// </summary>
    private void RebuildSoulChips()
    {
        foreach (Chip chip in _soulChips)
            Destroy(chip.Root);
        _soulChips.Clear();


        // Every soul, not only those the known places evolve: a soul with no place yet is just
        // what the player is looking for, and its row then says which Wisdoms it needs.
        foreach (string soul in DeskIndex.AllEvolvableSouls()
                     .OrderBy(WorldIndex.LabelOf, StringComparer.OrdinalIgnoreCase))
        {
            string captured = soul;
            Chip chip = TextChip(_soulGrid, WorldIndex.LabelOf(soul), -1f,
                () => _soulFilter.Souls.Contains(captured),
                () => PickSoul(captured),
                SpriteOf(soul));
            chip.Update();
            _soulChips.Add(chip);
        }
    }

    /// <summary>Souls switch like radio buttons; clicking the picked one clears it.</summary>
    private void PickSoul(string soul)
    {
        bool wasPicked = _soulFilter.Souls.Contains(soul);
        _soulFilter.Souls.Clear();
        if (!wasPicked)
            _soulFilter.Souls.Add(soul);
    }

    /// <summary>The picked soul has no place yet: say which Wisdoms evolve it.</summary>
    private void AddNoEvolvePlaceRow(string soul)
    {
        string wisdoms = string.Join(" or ", DeskIndex.WisdomsEvolving(soul).Select(w => WorldIndex.LabelOf("w." + w)));
        AddRow(SpriteOf(soul), WorldIndex.LabelOf(soul),
            $"<color=#D9796A>No place you know evolves it: needs Evolve via {wisdoms}</color>",
            () => { }, 0, "window_x", new Color32(0xD9, 0x79, 0x6A, 0xFF));
    }

    private static string KindLabel(PlaceKind kind) =>
        kind == PlaceKind.Desk ? "Desks" : kind == PlaceKind.Bed ? "Beds" : kind == PlaceKind.Soul ? "Souls" : kind.ToString();

    private void AddDeskRow(DeskEntry desk)
    {
        // Busy: amber, with the running recipe and time left; done: green, waiting to be collected.
        string state = desk.IsWorking ? $"<color=#E0A84F>{desk.WorkDescription()}</color>"
            : desk.IsDone ? "<color=#9CCB7A>Done: collect</color>"
            : desk.StateLabel;
        string subtitle = KindLabel(desk.Kind) + " · "
            + (desk.Location.Length > 0 ? desk.Location + " · " : "") + state;
        if (desk.Evolves.Count > 0)
            subtitle += " · Evolves via " + string.Join(", ", desk.Evolves);
        // One soul picked under Souls: say whether it can be evolved here now, or why not.
        // Green with a check mark when it can be done now, red with a cross and the reason when not.
        string mark = null;
        Color markColor = default;
        if (_tab == Tab.Souls && _soulFilter.Souls.Count == 1)
        {
            string verdict = EvolvePlanner.Verdict(desk, _soulFilter.Souls.First(), _items, out bool ready);
            subtitle = $"<color={(ready ? "#9CCB7A" : "#D9796A")}>{verdict}</color> · {subtitle}";
            mark = ready ? "slot yes" : "window_x";
            markColor = ready ? new Color32(0x9C, 0xCB, 0x7A, 0xFF) : new Color32(0xD9, 0x79, 0x6A, 0xFF);
        }
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
        Transform row = AddRow(art, desk.Name, subtitle, () => Focus(desk.Token, desk.Situation, desk.Name), 0, mark, markColor,
            onDoubleClick: () => OpenPlace(desk));
        if (desk.IsWorking)
            Fade(row); // so the free places stand out
        // Selected principles first; desks have no values, just the four they work with.
        foreach (string principle in desk.Principles.OrderByDescending(_deskFilter.Principles.Contains))
        {
            AspectIcon(row, principle, 24f);
        }
        // Only where it can be done now: under a red place the skills would just add noise.
        if (_tab == Tab.Souls && _soulFilter.Souls.Count == 1)
        {
            EvolvePlanner.Verdict(desk, _soulFilter.Souls.First(), _items, out bool ready);
            if (ready)
                AddEvolveSkillRows(desk, _soulFilter.Souls.First());
        }
    }

    /// <summary>
    /// Under a soul place: the player's skills attuned to the picked soul, the one(s) to use here
    /// first (green check), the others with why not (wrong Wisdom, slot principles, not committed).
    /// Saves trying every skill in the slot by hand.
    /// </summary>
    private void AddEvolveSkillRows(DeskEntry desk, string soul)
    {
        Color green = new Color32(0x9C, 0xCB, 0x7A, 0xFF);
        Color red = new Color32(0xD9, 0x79, 0x6A, 0xFF);
        foreach (EvolvePlanner.SkillOption option in EvolvePlanner.SkillOptions(desk, soul, _items).Where(o => o.Usable))
        {
            ItemEntry skill = option.Skill;
            string note = $"<color={(option.Usable ? "#9CCB7A" : "#D9796A")}>{option.Note}</color>";
            Transform row = AddRow(SpriteOf(skill.Stack.Element), skill.Title, note, () => FocusItem(skill), 40,
                option.Usable ? "slot yes" : "window_x", option.Usable ? green : red, () => SendToOpenWindow(skill));
            foreach (KeyValuePair<string, int> principle in skill.Aspects
                         .Where(a => a.Value > 0 && WorldIndex.IsPrinciple(a.Key))
                         .OrderByDescending(a => desk.Principles.Contains(a.Key))
                         .ThenByDescending(a => a.Value))
                AspectBadge(row, principle.Key, principle.Value, 22f, 16f);
        }
    }

    /// <summary>
    /// Aspect icon in a result row. Named "Aspect_&lt;id&gt;" and hit by raycasts so AspectPin
    /// can pin it with a middle click (it reads the id from the name: the mods share no types).
    /// Left clicks still reach the row, the nearest click handler up the hierarchy.
    /// </summary>
    private static Image AspectIcon(Transform row, string aspectId, float size)
    {
        Image icon = GameStyle.Panel(row, "Aspect_" + aspectId, "", Color.white);
        icon.sprite = SpriteOf(aspectId);
        icon.preserveAspect = true;
        GameStyle.Size(icon, size, size);
        return icon;
    }

    private static void AspectBadge(Transform row, string aspectId, int value, float iconSize, float fontSize)
    {
        AspectIcon(row, aspectId, iconSize);
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

/// <summary>Left click: Single after a short wait, unless a second click turns it into Double.</summary>
public class SingleOrDoubleClick : MonoBehaviour, IPointerClickHandler
{
    private const float Window = 0.3f;
    public Action Single;
    public Action Double;
    private float _singleAt = -1f;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;
        if (eventData.clickCount >= 2)
        {
            _singleAt = -1f;
            Double?.Invoke();
        }
        else
        {
            _singleAt = Time.unscaledTime + Window;
        }
    }

    private void Update()
    {
        if (_singleAt > 0f && Time.unscaledTime >= _singleAt)
        {
            _singleAt = -1f;
            Single?.Invoke();
        }
    }
}

public class HoverHandler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Action Enter;
    public Action Exit;

    public void OnPointerEnter(PointerEventData eventData) => Enter?.Invoke();
    public void OnPointerExit(PointerEventData eventData) => Exit?.Invoke();
}

/// <summary>
/// Drag a deck card along its row; on release it goes to the place the pointer is over (counted by
/// the other cards' centres). The row's layout reflows while the card floats. With a Target row the
/// card moves freely and, released over Target, is inserted there (Inserted); a plain click without
/// dragging calls Clicked.
/// </summary>
public class DeckCardDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    public int Index;
    public RectTransform Row;
    public RectTransform Target;
    public Action<int, int> Dropped;
    public Action<int> Inserted;
    public Action Clicked;
    private LayoutElement _layout;
    private CanvasGroup _group;

    public void OnBeginDrag(PointerEventData eventData)
    {
        _layout = GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
        _group = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0.7f;
        _group.blocksRaycasts = false;
        _layout.ignoreLayout = true;
        transform.SetAsLastSibling();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(Row, eventData.position, eventData.pressEventCamera, out Vector3 world))
            transform.position = Target != null ? world : new Vector3(world.x, transform.position.y, transform.position.z);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!eventData.dragging && eventData.button == PointerEventData.InputButton.Left)
            Clicked?.Invoke();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (Target != null)
        {
            if (!RectTransformUtility.RectangleContainsScreenPoint(Target, eventData.position, eventData.pressEventCamera))
            {
                Dropped?.Invoke(Index, Index);
                return;
            }
            int at = 0;
            foreach (Transform card in Target)
            {
                if (card.GetComponent<DeckCardDrag>() != null && card.position.x < transform.position.x)
                    at++;
            }
            Inserted?.Invoke(at);
            return;
        }
        float x = transform.position.x;
        int to = 0;
        foreach (Transform other in Row)
        {
            if (other == transform || other.GetComponent<DeckCardDrag>() == null)
                continue;
            if (other.position.x < x)
                to++;
        }
        Dropped?.Invoke(Index, to);
    }
}

/// <summary>
/// Middle click on this graphic runs Click; other clicks go on to the parent (the row's button),
/// so the row still works as before.
/// </summary>
public class MiddleClick : MonoBehaviour, IPointerClickHandler
{
    public Action Click;

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Middle)
            Click?.Invoke();
        else if (transform.parent != null)
            ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, eventData, ExecuteEvents.pointerClickHandler);
    }
}
