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
    private string _query = string.Empty;

    public GuideViewModel(AppStateService state, GuideRecommendationService recommendations, GuideDataService data)
    {
        _state = state;
        _recommendations = recommendations;
        _data = data;
        Refresh();
    }

    public ObservableCollection<TodaySuggestion> Suggestions { get; } = [];
    public ObservableCollection<BirthdayRecord> Birthdays { get; } = [];
    public ObservableCollection<FishRecord> Fish { get; } = [];
    public ObservableCollection<CropRecord> Crops { get; } = [];
    public ObservableCollection<BundleRecord> Bundles { get; } = [];
    public ObservableCollection<RecipeRecord> Recipes { get; } = [];
    public Visibility EmptyVisibility => _state.CurrentSave is null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SuggestionVisibility => _state.CurrentSave is null ? Visibility.Collapsed : Visibility.Visible;
    public string SaveContext => _state.CurrentSave is null
        ? "尚未选择存档"
        : $"{_state.CurrentSave.FarmerName} · {_state.CurrentSave.DateDisplay}";
    public string GoalSuggestion => _state.CurrentSave is null
        ? "扫描存档后可生成目标建议。"
        : _state.CurrentSave.CommunityCenterProgress < 100
            ? "优先补齐当前季节可获得的社区中心物品。"
            : "社区中心进度良好，可规划收入与好感目标。";

    public void Search(string query)
    {
        _query = query?.Trim() ?? string.Empty;
        Refresh();
    }

    public void Refresh()
    {
        Replace(Suggestions, Filter(_recommendations.Generate(_state.CurrentSave), item => $"{item.Title} {item.Description}"));
        Replace(Birthdays, Filter(_data.Birthdays, item => $"{item.Npc} {item.Season} {item.LovedGiftHint}"));
        Replace(Fish, Filter(_data.Fish, item => $"{item.Name} {item.Detail}"));
        Replace(Crops, Filter(_data.Crops, item => $"{item.Name} {item.Detail}"));
        Replace(Bundles, Filter(_data.Bundles, item => $"{item.Name} {item.Detail}"));
        Replace(Recipes, Filter(_data.Recipes, item => $"{item.Name} {item.Detail}"));
        OnPropertyChanged(nameof(EmptyVisibility));
        OnPropertyChanged(nameof(SuggestionVisibility));
        OnPropertyChanged(nameof(SaveContext));
        OnPropertyChanged(nameof(GoalSuggestion));
    }

    private IEnumerable<T> Filter<T>(IEnumerable<T> source, Func<T, string> text)
    {
        return string.IsNullOrWhiteSpace(_query)
            ? source
            : source.Where(item => text(item).Contains(_query, StringComparison.CurrentCultureIgnoreCase));
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
