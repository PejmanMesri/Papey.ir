namespace Papey.Services;

/// <summary>Refresh open storefronts after the administrator changes carousel settings.</summary>
public sealed class HomepageChanges(ILogger<HomepageChanges> logger)
{
    public event Action? Changed;
    public void Notify()
    {
        foreach (Action listener in Changed?.GetInvocationList() ?? [])
        {
            try { listener(); }
            catch (Exception ex) { logger.LogWarning(ex, "A storefront could not refresh homepage settings"); }
        }
    }
}
