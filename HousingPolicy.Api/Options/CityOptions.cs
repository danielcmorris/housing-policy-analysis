namespace HousingPolicy.Api.Options;

/// <summary>
/// Configuration for the city-legislation sync, bound from the "Cities"
/// section. Every configured city is a Granicus Legistar client — one adapter
/// covers hundreds of municipalities by swapping the client key.
/// </summary>
public sealed class CityOptions
{
    public const string SectionName = "Cities";

    public string LegistarBaseUrl { get; set; } = "https://webapi.legistar.com/v1";

    /// <summary>Legistar matter types worth tracking (agenda noise excluded).</summary>
    public string[] MatterTypes { get; set; } =
        { "Ordinance", "Resolution", "Charter Amendment", "Motion" };

    public CityClient[] Clients { get; set; } =
    {
        new() { Key = "sfgov", Name = "San Francisco", Jurisdiction = "San Francisco, CA" },
    };

    public sealed class CityClient
    {
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string Jurisdiction { get; set; } = "";
    }
}
