namespace ResourceManager.Models;

public enum WebResourceType
{
    WebPage = 1,
    CssStylesheet = 2,
    Script = 3,
    Data = 4,
    Png = 5,
    Jpg = 6,
    Gif = 7,
    Silverlight = 8,
    Xsl = 9,
    Ico = 10,
    Vector = 11,
    Resx = 12
}

public sealed class WebResourceItem
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required WebResourceType ResourceType { get; init; }
    public string? Description { get; init; }
    public bool IsManaged { get; init; }
    public DateTime? ModifiedOn { get; init; }

    public string TypeLabel => ResourceType switch
    {
        WebResourceType.WebPage       => "HTML",
        WebResourceType.CssStylesheet => "CSS",
        WebResourceType.Script        => "JavaScript",
        WebResourceType.Data          => "XML",
        WebResourceType.Png           => "PNG",
        WebResourceType.Jpg           => "JPG",
        WebResourceType.Gif           => "GIF",
        WebResourceType.Ico           => "ICO",
        WebResourceType.Vector        => "SVG",
        WebResourceType.Resx          => "RESX",
        WebResourceType.Xsl           => "XSL",
        _                             => ResourceType.ToString()
    };

    public string TypeBadge => ResourceType switch
    {
        WebResourceType.WebPage       => "HTML",
        WebResourceType.CssStylesheet => "CSS",
        WebResourceType.Script        => "JS",
        WebResourceType.Data          => "XML",
        WebResourceType.Png           => "PNG",
        WebResourceType.Jpg           => "JPG",
        WebResourceType.Gif           => "GIF",
        WebResourceType.Ico           => "ICO",
        WebResourceType.Vector        => "SVG",
        WebResourceType.Resx          => "RES",
        WebResourceType.Xsl           => "XSL",
        _                             => "?"
    };

    public string TypeColor => ResourceType switch
    {
        WebResourceType.Script        => "#007AFF",
        WebResourceType.CssStylesheet => "#AF52DE",
        WebResourceType.WebPage       => "#FF9500",
        WebResourceType.Data          => "#34C759",
        WebResourceType.Xsl           => "#34C759",
        WebResourceType.Resx          => "#5AC8FA",
        WebResourceType.Png or
        WebResourceType.Jpg or
        WebResourceType.Gif or
        WebResourceType.Ico or
        WebResourceType.Vector        => "#FF2D55",
        _                             => "#8E8E93"
    };

    public string ShortName
    {
        get
        {
            var slash = Name.LastIndexOf('/');
            return slash >= 0 ? Name[(slash + 1)..] : Name;
        }
    }

    public string FolderPath
    {
        get
        {
            var slash = Name.LastIndexOf('/');
            return slash >= 0 ? Name[..slash] : string.Empty;
        }
    }

    public bool HasFolder => Name.Contains('/');

    public string ModifiedOnDisplay =>
        ModifiedOn.HasValue ? ModifiedOn.Value.ToString("yyyy-MM-dd HH:mm") : string.Empty;
}
