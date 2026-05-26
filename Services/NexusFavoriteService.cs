using LSMA.Models;
using LSMA.Utilities;

namespace LSMA.Services;

public sealed class NexusFavoriteService(LoggingService logging)
{
    public async Task<List<NexusFavorite>> LoadAsync()
    {
        try
        {
            return File.Exists(AppPaths.FavoritesFile)
                ? await JsonHelper.ReadAsync<List<NexusFavorite>>(AppPaths.FavoritesFile) ?? []
                : [];
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync("读取收藏列表失败", exception);
            return [];
        }
    }

    public async Task SaveAsync(IReadOnlyList<NexusFavorite> favorites)
    {
        try
        {
            await JsonHelper.WriteAsync(AppPaths.FavoritesFile, favorites);
        }
        catch (Exception exception)
        {
            await logging.ErrorAsync("保存收藏列表失败", exception);
            throw;
        }
    }

    public async Task ToggleAsync(NexusModInfo mod, List<NexusFavorite> favorites)
    {
        var existing = favorites.FirstOrDefault(value => value.ModId == mod.ModId);
        if (existing is null)
        {
            favorites.Add(new NexusFavorite
            {
                ModId = mod.ModId,
                Name = mod.Name,
                Author = mod.Author,
                Version = mod.Version
            });
        }
        else
        {
            favorites.Remove(existing);
        }

        await SaveAsync(favorites);
    }
}
