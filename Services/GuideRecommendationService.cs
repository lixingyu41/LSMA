using LSMA.Models;

namespace LSMA.Services;

public sealed class GuideRecommendationService(GuideDataService data)
{
    public IReadOnlyList<TodaySuggestion> Generate(SaveInfo? save)
    {
        if (save is null)
        {
            return [];
        }

        var suggestions = new List<TodaySuggestion>();
        var remainingDays = Math.Max(0, 28 - save.Day);
        suggestions.Add(new TodaySuggestion
        {
            Category = "日历",
            Title = $"{save.Season}第 {save.Day} 日",
            Description = $"本季还剩 {remainingDays} 天，可据此安排作物收获和资源准备。",
            IconUri = save.SeasonIconUri
        });

        var birthday = data.Birthdays.FirstOrDefault(item => item.Season == save.Season && item.Day == save.Day);
        var tomorrow = data.Birthdays.FirstOrDefault(item => item.Season == save.Season && item.Day == save.Day + 1);
        if (birthday is not null)
        {
            suggestions.Add(new TodaySuggestion
            {
                Category = "生日",
                Title = $"今天是 {birthday.Npc} 的生日",
                Description = $"礼物提示：{birthday.LovedGiftHint}。",
                IconUri = birthday.IconUri
            });
        }
        else if (tomorrow is not null)
        {
            suggestions.Add(new TodaySuggestion
            {
                Category = "生日",
                Title = $"明天是 {tomorrow.Npc} 的生日",
                Description = $"今天可提前准备：{tomorrow.LovedGiftHint}。",
                IconUri = tomorrow.IconUri
            });
        }

        if (!string.IsNullOrWhiteSpace(save.Weather))
        {
            suggestions.Add(new TodaySuggestion
            {
                Category = "天气",
                Title = $"天气参考：{save.Weather}",
                Description = "出门前结合天气安排农活与采集路线。"
            });
        }

        var weatherFish = data.Fish.FirstOrDefault(fish => fish.Season.Contains(save.Season, StringComparison.Ordinal)
            && (fish.Weather == "任意" || fish.Weather == save.Weather));
        if (weatherFish is not null)
        {
            suggestions.Add(new TodaySuggestion
            {
                Category = "钓鱼",
                Title = $"可关注 {weatherFish.Name}",
                Description = $"{weatherFish.Location}，{weatherFish.Time}。{(weatherFish.CommunityCenterNeeded ? "社区中心可能需要。" : string.Empty)}",
                IconUri = weatherFish.IconUri
            });
        }

        var crop = data.Crops.FirstOrDefault(item => item.Season == save.Season && item.GrowDays <= remainingDays);
        if (crop is not null)
        {
            suggestions.Add(new TodaySuggestion
            {
                Category = "种植",
                Title = $"{crop.Name} 仍来得及成熟",
                Description = $"成熟需要 {crop.GrowDays} 天，本季剩余 {remainingDays} 天。",
                IconUri = crop.IconUri
            });
        }

        var weakest = save.Skills.OrderBy(skill => skill.Level).FirstOrDefault();
        if (weakest is not null)
        {
            suggestions.Add(new TodaySuggestion
            {
                Category = "技能",
                Title = $"提升{weakest.Name}技能",
                Description = $"当前等级 {weakest.Level}，可优先安排相关活动平衡发展。",
                IconUri = weakest.IconUri
            });
        }

        var closeFriend = save.Friendships
            .Where(friend => friend.Points % 250 >= 180 && friend.Hearts < 10)
            .OrderByDescending(friend => friend.Points % 250)
            .FirstOrDefault();
        if (closeFriend is not null)
        {
            suggestions.Add(new TodaySuggestion
            {
                Category = "好感",
                Title = $"{closeFriend.Name} 接近下一颗心",
                Description = "今天安排交谈或合适礼物，可能推进好感等级。",
                IconUri = closeFriend.IconUri
            });
        }

        if (save.LatestBackup is null || save.LatestBackup < DateTime.Now.AddDays(-7))
        {
            suggestions.Add(new TodaySuggestion
            {
                Category = "安全",
                Title = "建议备份当前存档",
                Description = "最近没有可用备份或备份已超过 7 天，可在存档页一键创建。"
            });
        }

        return suggestions;
    }
}
