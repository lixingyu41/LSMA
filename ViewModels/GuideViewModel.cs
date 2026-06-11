using System.Collections.ObjectModel;
using LSMA.Models;
using LSMA.Services;
using LSMA.Utilities;
using Microsoft.UI.Xaml;

namespace LSMA.ViewModels;

public sealed class GuideViewModel : ViewModelBase
{
    public const string SuggestionsSectionKey = "suggestions";
    public const string SearchSectionKey = "search";
    public const string CropsSectionKey = "crops";
    public const string FishSectionKey = "fish";
    public const string FestivalsSectionKey = "festivals";
    public const string BundlesSectionKey = "bundles";

    private readonly AppStateService _state;
    private readonly SettingsService _settings;
    private readonly GuideRecommendationService _recommendations;
    private readonly GuideDataService _data;
    private readonly GameIconService _icons;
    private readonly GameContentCatalogService _catalog;
    private string _query = string.Empty;
    private bool _staticGuideIconsApplied;

    public GuideViewModel(
        AppStateService state,
        SettingsService settings,
        GuideRecommendationService recommendations,
        GuideDataService data,
        GameIconService icons,
        GameContentCatalogService catalog)
    {
        _state = state;
        _settings = settings;
        _recommendations = recommendations;
        _data = data;
        _icons = icons;
        _catalog = catalog;
        Refresh();
    }

    public ObservableCollection<TodaySuggestion> Suggestions { get; } = new RangeObservableCollection<TodaySuggestion>();
    public ObservableCollection<FestivalRecord> Festivals { get; } = new RangeObservableCollection<FestivalRecord>();
    public ObservableCollection<FishRecord> Fish { get; } = new RangeObservableCollection<FishRecord>();
    public ObservableCollection<CropRecord> Crops { get; } = new RangeObservableCollection<CropRecord>();
    public ObservableCollection<BundleRecord> Bundles { get; } = new RangeObservableCollection<BundleRecord>();
    public ObservableCollection<GuideSearchResult> SearchResults { get; } = new RangeObservableCollection<GuideSearchResult>();
    public bool IsSearchActive => !string.IsNullOrWhiteSpace(_query);
    public Visibility EmptyVisibility => _state.CurrentSave is null && !IsSearchActive ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SuggestionVisibility => _state.CurrentSave is not null && !IsSearchActive ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SearchVisibility => string.IsNullOrWhiteSpace(_query) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility StructuredVisibility => string.IsNullOrWhiteSpace(_query) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BundleVisibility => _state.CurrentSave is { } save
        ? _catalog.GetCommunityBundles(save).Count > 0 || save.CommunityCenterProgress < 100
            ? Visibility.Visible
            : Visibility.Collapsed
        : Visibility.Visible;
    public string FishHeading => _state.CurrentSave is null ? "可钓鱼类" : $"{_state.CurrentSave.DateDisplay}可钓鱼类";
    public string SearchSummary => $"“{_query}”的游戏内容结果：{SearchResults.Count} 项";
    public string SaveContext => _state.CurrentSave is null
        ? "尚未选择存档"
        : $"{_state.CurrentSave.FarmerName} · {_state.CurrentSave.DateDisplay}";
    public string GoalSuggestion => _state.CurrentSave is null
        ? "未发现可分析的存档。"
        : _catalog.GetCommunityBundles(_state.CurrentSave).Count > 0
            ? "优先补齐当前献祭缺项。"
            : "社区中心进度良好，可规划收入与好感目标。";
    public Visibility SuggestionsContentVisibility => SectionContentVisibility(SuggestionsSectionKey, SuggestionVisibility);
    public Visibility SearchContentVisibility => SectionContentVisibility(SearchSectionKey, SearchVisibility);
    public Visibility CropsContentVisibility => SectionContentVisibility(CropsSectionKey, StructuredVisibility);
    public Visibility FishContentVisibility => SectionContentVisibility(FishSectionKey, StructuredVisibility);
    public Visibility FestivalsContentVisibility => SectionContentVisibility(FestivalsSectionKey, StructuredVisibility);
    public Visibility BundlesContentVisibility => SectionContentVisibility(BundlesSectionKey, BundleVisibility);
    public string SuggestionsToggleGlyph => SectionToggleGlyph(SuggestionsSectionKey);
    public string SearchToggleGlyph => SectionToggleGlyph(SearchSectionKey);
    public string CropsToggleGlyph => SectionToggleGlyph(CropsSectionKey);
    public string FishToggleGlyph => SectionToggleGlyph(FishSectionKey);
    public string FestivalsToggleGlyph => SectionToggleGlyph(FestivalsSectionKey);
    public string BundlesToggleGlyph => SectionToggleGlyph(BundlesSectionKey);

    public async Task ToggleSectionAsync(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        key = key.Trim();
        var collapsed = !IsSectionCollapsed(key);
        await _settings.UpdateAsync(settings => settings.GuideCollapsedSections[key] = collapsed);
        NotifySectionStateChanged(key);
    }

    private Visibility SectionContentVisibility(string key, Visibility sectionVisibility)
    {
        return sectionVisibility == Visibility.Visible && !IsSectionCollapsed(key)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private bool IsSectionCollapsed(string key)
    {
        return _settings.Current.GuideCollapsedSections.TryGetValue(key, out var collapsed) && collapsed;
    }

    private string SectionToggleGlyph(string key)
    {
        return IsSectionCollapsed(key) ? "\uE70D" : "\uE70E";
    }

    private void NotifySectionStateChanged(string key)
    {
        switch (key)
        {
            case SuggestionsSectionKey:
                OnPropertyChanged(nameof(SuggestionsContentVisibility));
                OnPropertyChanged(nameof(SuggestionsToggleGlyph));
                break;
            case SearchSectionKey:
                OnPropertyChanged(nameof(SearchContentVisibility));
                OnPropertyChanged(nameof(SearchToggleGlyph));
                break;
            case CropsSectionKey:
                OnPropertyChanged(nameof(CropsContentVisibility));
                OnPropertyChanged(nameof(CropsToggleGlyph));
                break;
            case FishSectionKey:
                OnPropertyChanged(nameof(FishContentVisibility));
                OnPropertyChanged(nameof(FishToggleGlyph));
                break;
            case FestivalsSectionKey:
                OnPropertyChanged(nameof(FestivalsContentVisibility));
                OnPropertyChanged(nameof(FestivalsToggleGlyph));
                break;
            case BundlesSectionKey:
                OnPropertyChanged(nameof(BundlesContentVisibility));
                OnPropertyChanged(nameof(BundlesToggleGlyph));
                break;
        }
    }

    private void NotifyAllSectionStatesChanged()
    {
        NotifySectionStateChanged(SuggestionsSectionKey);
        NotifySectionStateChanged(SearchSectionKey);
        NotifySectionStateChanged(CropsSectionKey);
        NotifySectionStateChanged(FishSectionKey);
        NotifySectionStateChanged(FestivalsSectionKey);
        NotifySectionStateChanged(BundlesSectionKey);
    }

    public async Task SearchAsync(string query)
    {
        _query = query?.Trim() ?? string.Empty;

        Refresh();
        SearchNpcGifts();
        await EnsureSearchResultIconsAsync();
        Replace(SearchResults, SearchResults.ToList());
        OnPropertyChanged(nameof(SearchSummary));
    }

    private void SearchNpcGifts()
    {
        if (string.IsNullOrWhiteSpace(_query)) return;

        var giftQueries = GiftQueryTerms(_query).ToList();
        foreach (var gift in _data.NpcGifts
            .Where(g => g.Npc.Contains(_query, StringComparison.CurrentCultureIgnoreCase)
                || giftQueries.Any(query => GiftFieldsContain(g, query))))
        {
            gift.IconUri ??= _icons.GetPortraitUri(gift.NpcId);
            var result = SearchResults.FirstOrDefault(item => item.NpcId == gift.NpcId || item.Title == gift.Npc);
            if (result is null)
            {
                result = new GuideSearchResult
                {
                    Category = "人物",
                    Title = gift.Npc,
                    Detail = $"{gift.Birthday} 生日 · 喜爱：{gift.Loves}",
                    NpcId = gift.NpcId,
                    IconUri = gift.IconUri
                };
                SearchResults.Add(result);
            }

            if (!gift.Npc.Equals(_query, StringComparison.CurrentCultureIgnoreCase)
                && GiftMatchReason(gift, giftQueries) is { } matchReason)
            {
                AddMatchReason(result, matchReason);
            }

            AddGiftSection(result, "喜爱 +80", gift.Loves);
            AddGiftSection(result, "喜欢 +45", gift.Likes);
            AddGiftSection(result, "中立 +20", gift.Neutral);
            AddGiftSection(result, "不喜欢 -20", gift.Dislikes);
            AddGiftSection(result, "讨厌 -40", gift.Hates);
        }
    }

    private static bool GiftFieldsContain(NpcGiftRecord gift, string query)
    {
        return gift.Loves.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || gift.Likes.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || gift.Neutral.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || gift.Dislikes.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || gift.Hates.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private static IEnumerable<string> GiftQueryTerms(string query)
    {
        yield return query;
        foreach (var alias in query switch
        {
            "黄水仙" => ["水仙花"],
            "野山葵" => ["辣根"],
            "粘土" => ["黏土"],
            "黏土" => ["粘土"],
            _ => Array.Empty<string>()
        })
        {
            yield return alias;
        }
    }

    private static string? GiftMatchReason(NpcGiftRecord gift, IReadOnlyList<string> queries)
    {
        var query = queries.FirstOrDefault(value => gift.Loves.Contains(value, StringComparison.CurrentCultureIgnoreCase));
        if (query is not null)
        {
            return $"喜爱礼物包含：{query}";
        }

        query = queries.FirstOrDefault(value => gift.Likes.Contains(value, StringComparison.CurrentCultureIgnoreCase));
        if (query is not null)
        {
            return $"喜欢礼物包含：{query}";
        }

        query = queries.FirstOrDefault(value => gift.Neutral.Contains(value, StringComparison.CurrentCultureIgnoreCase));
        if (query is not null)
        {
            return $"中立礼物包含：{query}";
        }

        query = queries.FirstOrDefault(value => gift.Dislikes.Contains(value, StringComparison.CurrentCultureIgnoreCase));
        if (query is not null)
        {
            return $"不喜欢礼物包含：{query}";
        }

        query = queries.FirstOrDefault(value => gift.Hates.Contains(value, StringComparison.CurrentCultureIgnoreCase));
        return query is not null
            ? $"讨厌礼物包含：{query}"
            : null;
    }

    private static void AddMatchReason(GuideSearchResult result, string reason)
    {
        if (result.Sections.Any(section => section.Title == "匹配原因"
            && section.Lines.Any(line => line.Equals(reason, StringComparison.CurrentCultureIgnoreCase))))
        {
            return;
        }

        var section = result.Sections.FirstOrDefault(section => section.Title == "匹配原因");
        if (section is null)
        {
            section = new GuideSearchSection { Title = "匹配原因" };
            result.Sections.Insert(0, section);
        }

        section.Lines.Add(reason);
    }

    private async Task EnsureSearchResultIconsAsync()
    {
        foreach (var result in SearchResults)
        {
            result.IconUri = result.IconTexture is { Length: > 0 } texture && result.IconSpriteIndex is { } spriteIndex
                ? await _icons.GetTextureIconAsync(texture, spriteIndex, result.IconWidth, result.IconHeight)
                : result.ObjectId is { } objectId
                    ? await _icons.GetObjectIconAsync(objectId)
                    : result.NpcId is { } npcId ? _icons.GetPortraitUri(npcId) : result.IconUri;

            foreach (var action in result.Sections.SelectMany(section => section.Actions))
            {
                if (action.IconTexture is { Length: > 0 } actionTexture && action.IconSpriteIndex is { } actionSpriteIndex)
                {
                    action.IconUri = await _icons.GetTextureIconAsync(actionTexture, actionSpriteIndex, action.IconWidth, action.IconHeight);
                }
                else if ((action.ObjectId ?? _catalog.FindObjectIdByName(action.Query)) is { } id)
                {
                    action.IconUri = await _icons.GetObjectIconAsync(id);
                }
            }
        }
    }

    private void AddGiftSection(GuideSearchResult result, string title, string rawItems)
    {
        if (result.Sections.Any(section => section.Title == title))
        {
            return;
        }

        var actions = SplitGiftItems(rawItems)
            .Select(item => new GuideSearchAction
            {
                Label = item,
                Query = item,
                ObjectId = _catalog.FindObjectIdByName(item)
            })
            .ToList();
        if (actions.Count == 0)
        {
            return;
        }

        var section = new GuideSearchSection { Title = title };
        section.Actions.AddRange(actions);
        result.Sections.Add(section);
    }

    private static IEnumerable<string> SplitGiftItems(string value)
    {
        return value
            .Split([',', '，', '、'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length > 0);
    }

    public async Task RefreshAsync()
    {
        await EnsureStructuredIconsAsync();
        Refresh();
        if (IsSearchActive)
        {
            SearchNpcGifts();
            await EnsureSearchResultIconsAsync();
            Replace(SearchResults, SearchResults.ToList());
            OnPropertyChanged(nameof(SearchSummary));
        }
    }

    public void Refresh()
    {
        ApplyStaticGuideIcons();

        var festivals = _catalog.GetUpcomingFestivals(_state.CurrentSave);
        foreach (var festival in festivals)
        {
            festival.IconUri = GetSeasonIcon(festival.Season);
        }

        var fishToday = _catalog.GetFishToday(_state.CurrentSave);
        foreach (var fish in fishToday)
        {
            fish.IconUri = _icons.GetObjectIconUri(fish.ObjectId);
        }

        var crops = _catalog.GetCropsForCurrentSeason(_state.CurrentSave);
        foreach (var crop in crops)
        {
            crop.IconUri = _icons.GetObjectIconUri(crop.ObjectId);
        }

        var bundles = _catalog.GetCommunityBundles(_state.CurrentSave);
        foreach (var bundle in bundles)
        {
            bundle.IconUri = _icons.GetObjectIconUri(bundle.ObjectId);
            foreach (var item in bundle.Items)
            {
                item.IconUri = _icons.GetObjectIconUri(item.ObjectId);
            }
        }

        Replace(Suggestions, _recommendations.Generate(_state.CurrentSave));
        Replace(Festivals, festivals);
        Replace(Fish, (fishToday.Count > 0 ? fishToday : _data.Fish)
            .OrderByDescending(fish => fish.SalePrice)
            .ThenByDescending(fish => fish.IsLegendary)
            .ThenBy(fish => fish.SortStartMinutes)
            .ThenBy(fish => fish.Location)
            .ThenBy(fish => fish.Name));
        Replace(Crops, crops.Count > 0 ? crops : _data.Crops);
        Replace(Bundles, bundles.Count > 0 ? bundles : _data.Bundles);
        Replace(SearchResults, _catalog.Search(_query));
        foreach (var result in SearchResults)
        {
            result.IconUri = result.IconTexture is { Length: > 0 } texture && result.IconSpriteIndex is { } spriteIndex
                ? _icons.GetTextureIconUri(texture, spriteIndex, result.IconWidth, result.IconHeight)
                : result.ObjectId is { } objectId
                ? _icons.GetObjectIconUri(objectId)
                : result.NpcId is { } npcId ? _icons.GetPortraitUri(npcId) : null;

            foreach (var action in result.Sections.SelectMany(section => section.Actions))
            {
                if (action.IconTexture is { Length: > 0 } actionTexture && action.IconSpriteIndex is { } actionSpriteIndex)
                {
                    action.IconUri = _icons.GetTextureIconUri(actionTexture, actionSpriteIndex, action.IconWidth, action.IconHeight);
                }
                else if ((action.ObjectId ?? _catalog.FindObjectIdByName(action.Query)) is { } id)
                {
                    action.IconUri = _icons.GetObjectIconUri(id);
                }
            }
        }

        OnPropertyChanged(nameof(EmptyVisibility));
        OnPropertyChanged(nameof(SuggestionVisibility));
        OnPropertyChanged(nameof(IsSearchActive));
        OnPropertyChanged(nameof(SearchVisibility));
        OnPropertyChanged(nameof(StructuredVisibility));
        OnPropertyChanged(nameof(BundleVisibility));
        OnPropertyChanged(nameof(FishHeading));
        OnPropertyChanged(nameof(SearchSummary));
        OnPropertyChanged(nameof(SaveContext));
        OnPropertyChanged(nameof(GoalSuggestion));
        NotifyAllSectionStatesChanged();
    }

    private async Task EnsureStructuredIconsAsync()
    {
        foreach (var fish in _catalog.GetFishToday(_state.CurrentSave))
        {
            await _icons.GetObjectIconAsync(fish.ObjectId);
        }

        foreach (var crop in _catalog.GetCropsForCurrentSeason(_state.CurrentSave))
        {
            await _icons.GetObjectIconAsync(crop.ObjectId);
        }

        foreach (var bundle in _catalog.GetCommunityBundles(_state.CurrentSave))
        {
            await _icons.GetObjectIconAsync(bundle.ObjectId);
            foreach (var item in bundle.Items)
            {
                await _icons.GetObjectIconAsync(item.ObjectId);
            }
        }
    }

    private void ApplyStaticGuideIcons()
    {
        if (_staticGuideIconsApplied)
        {
            return;
        }

        var allResolved = true;
        foreach (var record in _data.Birthdays)
        {
            record.IconUri = _icons.GetPortraitUri(record.NpcId);
            allResolved &= record.IconUri is not null;
        }

        foreach (var record in _data.Fish)
        {
            record.IconUri = _icons.GetObjectIconUri(record.ObjectId);
            allResolved &= record.IconUri is not null;
        }

        foreach (var record in _data.Crops)
        {
            record.IconUri = _icons.GetObjectIconUri(record.ObjectId);
            allResolved &= record.IconUri is not null;
        }

        foreach (var record in _data.Bundles)
        {
            record.IconUri = _icons.GetObjectIconUri(record.ObjectId);
            allResolved &= record.IconUri is not null;
            foreach (var item in record.Items)
            {
                item.IconUri = _icons.GetObjectIconUri(item.ObjectId);
                allResolved &= item.IconUri is not null;
            }
        }

        _staticGuideIconsApplied = _state.IsGameConfigured && allResolved;
    }

    private string? GetSeasonIcon(string season)
    {
        return _icons.GetObjectIconUri(season switch
        {
            "春季" => 24,
            "夏季" => 258,
            "秋季" => 276,
            "冬季" => 414,
            _ => 24
        });
    }

    private static void Replace<T>(ObservableCollection<T> destination, IEnumerable<T> source)
    {
        if (destination is RangeObservableCollection<T> range)
        {
            range.ReplaceWith(source);
            return;
        }

        destination.Clear();
        foreach (var item in source)
        {
            destination.Add(item);
        }
    }
}
