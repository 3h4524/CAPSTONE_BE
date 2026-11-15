namespace APCS.Application.Abstractions.Commerce;

/// <summary>
/// The platform's own Printify token, which admins use to read the catalog and to make templates of Printify
/// products. Null when the server has none.
/// </summary>
public interface IPrintifyAdminToken
{
    string? Token { get; }
}
