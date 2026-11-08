namespace Papey.Services;
// One shared read per request/circuit, invalidated when the owner saves settings.
public sealed class StorefrontPricing(TaxSettingsService settings) : IDisposable {
 private readonly object _gate=new(); private Task<int>? _rate;
 public event Action? Changed;
 private bool _subscribed;
 public Task<int> RateAsync(){lock(_gate){if(!_subscribed){settings.Changed+=Invalidate;_subscribed=true;}return _rate??=settings.DiscountAsync();}}
 private void Invalidate(){lock(_gate)_rate=null;Changed?.Invoke();}
 public void Dispose(){if(_subscribed)settings.Changed-=Invalidate;}
}
