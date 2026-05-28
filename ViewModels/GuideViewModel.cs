using System.Collections.ObjectModel;
using LSMA.Models;
using LSMA.Services;
using Microsoft.UI.Xaml;

namespace LSMA.ViewModels;

public sealed class GuideViewModel : ViewModelBase
{
    private readonly AppStateService _state;
    private readonly GuideRecommendationService _recommendations;
    private readonly GuideDataService _data;
    private readonly GameIconService _icons;
    private readonly GameContentCatalogService _catalog;
    private string _query = string.Empty;

    public GuideViewModel(
        AppStateService state,
        GuideRecommendationService recommendations,
        GuideDataService data,
        GameIconService icons,
        GameContentCatalogService catalog)
    {
        _state = state;
        _recommendations = recommendations;
        _data = data;
        _icons = icons;
        _catalog = catalog;
        Refresh();
    }

    public ObservableCollection<TodaySuggestion> Suggestions { get; } = [];
    public ObservableCollection<FestivalRecord> Festivals { get; } = [];
    public ObservableCollection<FishRecord> Fish { get; } = [];
    public ObservableCollection<CropRecord> Crops { get; } = [];
    public ObservableCollection<BundleRecord> Bundles { get; } = [];
    public ObservableCollection<GuideSearchResult> SearchResults { get; } = [];
    public Visibility EmptyVisibility => _state.CurrentSave is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SuggestionVisibility => _state.CurrentSave is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility SearchVisibility => string.IsNullOrWhiteSpace(_query) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility StructuredVisibility => string.IsNullOrWhiteSpace(_query) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BundleVisibility => _state.CurrentSave?.CommunityCenterProgress >= 100
        ? Visibility.Collapsed
        : Visibility.Visible;
    public string FishHeading => _state.CurrentSave is null ? "可钓鱼类" : $"{_state.CurrentSave.DateDisplay}可钓鱼类";
    public string SearchSummary => $"“{_query}”的游戏内容结果：{SearchResults.Count} 项";
    public string SaveContext => _state.CurrentSave is null
        ? "尚未选择存档"
        : $"{_state.CurrentSave.FarmerName} · {_state.CurrentSave.DateDisplay}";
    public string GoalSuggestion => _state.CurrentSave is null
        ? "未发现可分析的存档。"
        : _state.CurrentSave.CommunityCenterProgress < 100
            ? "优先补齐当前季节可获得的社区中心物品。"
            : "社区中心进度良好，可规划收入与好感目标。";

    public async Task SearchAsync(string query)
    {
        _query = query?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(_query))
        {
            foreach (var result in _catalog.Search(_query))
            {
                result.IconUri = result.IconTexture is { Length: > 0 } texture && result.IconSpriteIndex is { } spriteIndex
                    ? await _icons.GetTextureIconAsync(texture, spriteIndex)
                    : result.ObjectId is { } objectId
                    ? await _icons.GetObjectIconAsync(objectId)
                    : result.NpcId is { } npcId ? _icons.GetPortraitUri(npcId) : null;
            }
        }

        Refresh();
    }

    public async Task RefreshAsync()
    {
        foreach (var fish in _catalog.GetFishToday(_state.CurrentSave))
        {
            await _icons.GetObjectIconAsync(fish.ObjectId);
        }

        await SearchAsync(_query);
    }

    public void Refresh()
    {
        foreach (var record in _data.Birthdays)
        {
            record.IconUri = _icons.GetPortraitUri(record.NpcId);
        }

        foreach (var record in _data.Fish)
        {
            record.IconUri = _icons.GetObjectIconUri(record.ObjectId);
        }

        foreach (var record in _data.Crops)
        {
            record.IconUri = _icons.GetObjectIconUri(record.ObjectId);
        }

        foreach (var record in _data.Bundles)
        {
            record.IconUri = _icons.GetObjectIconUri(record.ObjectId);
        }

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

        Replace(Suggestions, _recommendations.Generate(_state.CurrentSave));
        Replace(Festivals, festivals);
        Replace(Fish, fishToday.Count > 0 ? fishToday : _data.Fish);
        Replace(Crops, _data.Crops);
        Replace(Bundles, _data.Bundles);
        Replace(SearchResults, _catalog.Search(_query));
        foreach (var result in SearchResults)
        {
            result.IconUri = result.IconTexture is { Length: > 0 } texture && result.IconSpriteIndex is { } spriteIndex
                ? _icons.GetTextureIconUri(texture, spriteIndex)
                : result.ObjectId is { } objectId
                ? _icons.GetObjectIconUri(objectId)
                : result.NpcId is { } npcId ? _icons.GetPortraitUri(npcId) : null;
        }

        OnPropertyChanged(nameof(EmptyVisibility));
        OnPropertyChanged(nameof(SuggestionVisibility));
        OnPropertyChanged(nameof(SearchVisibility));
        OnPropertyChanged(nameof(StructuredVisibility));
        OnPropertyChanged(nameof(BundleVisibility));
        OnPropertyChanged(nameof(FishHeading));
        OnPropertyChanged(nameof(SearchSummary));
        OnPropertyChanged(nameof(SaveContext));
        OnPropertyChanged(nameof(GoalSuggestion));
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
        destination.Clear();
        foreach (var item in source)
        {
            destination.Add(item);
        }
    }
}
