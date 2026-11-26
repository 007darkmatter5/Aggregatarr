using Aggregatarr.Models;
using Aggregatarr.Web.Services;
using Microsoft.AspNetCore.Components;

namespace Aggregatarr.Web.Components.Layout;

public abstract class LivePage : ComponentBase, IDisposable
{
    [Inject] protected DashboardStore Store { get; set; } = default!;

    protected DashboardSnapshot Snapshot => Store.Current;

    protected override void OnInitialized() => Store.Changed += OnChanged;

    private void OnChanged() => InvokeAsync(StateHasChanged);

    public void Dispose() => Store.Changed -= OnChanged;
}
