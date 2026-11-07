using MediaBrowser.Model.Dto;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.Plm;

/// <summary>A partial UserData write that omits IsFavorite is not an unlike.
/// Native dedicated favorite endpoints already use UpdateUserRating.</summary>
public sealed class NativeFavoriteIntent(FavoriteMutationGate gates) : IAsyncActionFilter
{
    private static readonly AsyncLocal<bool> Explicit = new();
    internal static bool HasFavoriteField => Explicit.Value;
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var previous=Explicit.Value;
        Explicit.Value=context.ActionArguments.Values.OfType<UpdateUserItemDataDto>().Any(d=>d.IsFavorite.HasValue)
            || context.ActionDescriptor.AttributeRouteInfo?.Template?.Contains("FavoriteItems",StringComparison.OrdinalIgnoreCase)==true;
        IDisposable? lease=null;
        try
        {
            if(Explicit.Value)
            {
                Guid.TryParse(context.HttpContext.User.FindFirst("Jellyfin-UserId")?.Value,out var owner);
                if(context.ActionArguments.TryGetValue("userId",out var ownerArgument) && ownerArgument is Guid requestedOwner && requestedOwner!=Guid.Empty)owner=requestedOwner;
                if(context.ActionArguments.TryGetValue("itemId",out var itemArgument) && itemArgument is Guid item && owner!=Guid.Empty)
                    lease=await gates.AcquireAsync(owner,item,context.HttpContext.RequestAborted);
            }
            await next();
        }
        finally { Explicit.Value=previous;lease?.Dispose(); }
    }
}
