using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.Plm;

/// <summary>A partial UserData write that omits IsFavorite is not an unlike.
/// Native dedicated favorite endpoints already use UpdateUserRating.</summary>
public sealed class NativeFavoriteIntent : IAsyncActionFilter
{
    private static readonly AsyncLocal<bool> Explicit = new();
    internal static bool HasFavoriteField => Explicit.Value;
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var previous=Explicit.Value;
        Explicit.Value=context.ActionArguments.Values.OfType<UpdateUserItemDataDto>().Any(d=>d.IsFavorite.HasValue)
            || context.ActionDescriptor.AttributeRouteInfo?.Template?.Contains("FavoriteItems",StringComparison.OrdinalIgnoreCase)==true;
        try { await next(); }
        finally { Explicit.Value=previous; }
    }
}
