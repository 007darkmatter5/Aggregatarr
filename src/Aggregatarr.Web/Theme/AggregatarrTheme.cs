using Aggregatarr.Models;
using MudBlazor;

namespace Aggregatarr.Web.Theme;

public static class AggregatarrTheme
{
    public static MudTheme Build() => new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#c56a24",
            Secondary = "#2f6fad",
            Tertiary = "#0f8f72",
            Background = "#f4f1eb",
            Surface = "#fffcf8",
            AppbarBackground = "#fffcf8",
            AppbarText = "#1c1915",
            DrawerBackground = "#fffcf8",
            DrawerText = "#1c1915",
            DrawerIcon = "#5c564c",
            TextPrimary = "#1c1915",
            TextSecondary = "#5c564c",
            Success = "#0f8f62",
            Warning = "#b86e00",
            Error = "#c43838",
            Info = "#2f6fad",
            Divider = "#1c191514",
            LinesDefault = "#1c19151f",
            TableLines = "#1c191514",
            ActionDefault = "#5c564c"
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#e8a15a",
            Secondary = "#8eb7ff",
            Tertiary = "#5ee0b5",
            Background = "#0c1017",
            Surface = "#151b26",
            AppbarBackground = "#10151ee6",
            AppbarText = "#e7edf6",
            DrawerBackground = "#10151e",
            DrawerText = "#e7edf6",
            DrawerIcon = "#93a0b4",
            TextPrimary = "#e7edf6",
            TextSecondary = "#93a0b4",
            Success = "#3ddc97",
            Warning = "#ffc14d",
            Error = "#ff6b6b",
            Info = "#8eb7ff",
            Divider = "#ffffff14",
            LinesDefault = "#ffffff1f",
            TableLines = "#ffffff14",
            ActionDefault = "#93a0b4",
            OverlayDark = "#00000088"
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "14px",
            DrawerWidthLeft = "248px"
        },
        Typography = new Typography
        {
            Default = new DefaultTypography
            {
                FontFamily = ["Figtree", "Segoe UI", "sans-serif"]
            },
            H1 = new H1Typography { FontFamily = ["Outfit", "Figtree", "sans-serif"], FontWeight = "640" },
            H2 = new H2Typography { FontFamily = ["Outfit", "Figtree", "sans-serif"], FontWeight = "640" },
            H3 = new H3Typography { FontFamily = ["Outfit", "Figtree", "sans-serif"], FontWeight = "620" },
            H4 = new H4Typography { FontFamily = ["Outfit", "Figtree", "sans-serif"], FontWeight = "620" },
            H5 = new H5Typography { FontFamily = ["Outfit", "Figtree", "sans-serif"], FontWeight = "600" },
            H6 = new H6Typography { FontFamily = ["Outfit", "Figtree", "sans-serif"], FontWeight = "600" },
            Button = new ButtonTypography { FontFamily = ["Outfit", "Figtree", "sans-serif"], FontWeight = "600" }
        }
    };
}

public static class ServiceVisuals
{
    public static string Icon(ServiceKind kind) => kind switch
    {
        ServiceKind.Sonarr => Icons.Material.Filled.LiveTv,
        ServiceKind.Radarr => Icons.Material.Filled.Movie,
        ServiceKind.Lidarr => Icons.Material.Filled.Album,
        ServiceKind.Prowlarr => Icons.Material.Filled.RssFeed,
        ServiceKind.Bazarr => Icons.Material.Filled.ClosedCaption,
        ServiceKind.QBittorrent => Icons.Material.Filled.Download,
        ServiceKind.Sabnzbd => Icons.Material.Filled.Inventory2,
        _ => Icons.Material.Filled.Dns
    };

    public static string Css(ServiceKind kind) => "kind-" + kind.ToString().ToLowerInvariant();
}
