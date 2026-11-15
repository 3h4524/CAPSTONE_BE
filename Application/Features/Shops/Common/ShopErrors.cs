using APCS.Common.Models;

namespace APCS.Application.Features.Shops.Common;

/// <summary>Creates expected errors returned by shop use cases.</summary>
public static class ShopErrors
{
    public static Error Unauthenticated() =>
        Error.Unauthorized("shop.unauthorized", "Please sign in to manage your shop.");

    public static Error EtsyNotConfigured() =>
        Error.Failure("shop.etsy_not_configured", "Etsy is not configured on this server yet. Contact the administrator.");

    public static Error NotConnected() =>
        Error.NotFound("shop.not_connected", "No Etsy shop is connected.");

    public static Error ReconnectRequired() =>
        Error.Conflict("shop.reconnect_required", "Connect your Etsy shop again on the Shop page, then push.");

    public static Error ProductNotFound() =>
        Error.NotFound("shop.product_not_found", "The product was not found.");

    public static Error MockupNotApproved() =>
        Error.Validation("Only approved mock-ups of this product can be sent to Etsy.");

    public static Error VideoNotApproved() =>
        Error.Validation("This product has no approved video to send.");

    public static Error EtsyUnavailable() =>
        Error.Failure("shop.etsy_unavailable", "Etsy is not answering right now. Try again in a moment.");

    /// <param name="reason">Etsy's own explanation, when it gave one.</param>
    public static Error PublishFailed(Abstractions.Commerce.EtsyFailureKind kind, string? reason = null) => kind == Abstractions.Commerce.EtsyFailureKind.RateLimited
        ? Error.Failure("shop.etsy_rate_limited", "Etsy is limiting requests. Try again in a minute.")
        : Error.Failure("shop.publish_failed", string.IsNullOrWhiteSpace(reason)
            ? "Etsy did not accept the listing. Check the category, shipping profile and price, then try again."
            : $"Etsy did not accept the listing: {reason}");

    public static Error ListingPace(string reason) => new("shop.listing_pace", reason, ErrorType.Conflict);

    public static Error NoRealPhoto() => Error.Validation(ListingPhotos.NoRealPhoto);

    public static Error NoProductionPartner() =>
        Error.Validation("Your Etsy shop lists no production partner, which Etsy requires for items Printify makes. Add Printify under Shop Manager > Settings > Production partners, then publish again.");

    public static Error DesignNotApproved() =>
        Error.Validation("Only approved designs of this product can be sent.");

    public static Error PrintifyNotConnected() =>
        Error.Conflict("shop.printify_not_connected", "Add your Printify API key on the API Keys page first.");

    public static Error PrintifyKeyInvalid() =>
        Error.Conflict("shop.printify_key_invalid", "Your Printify API key is not working. Check or replace it on the API Keys page.");

    public static Error PrintifyNoStore() =>
        Error.Conflict("shop.printify_no_store", "Your Printify account has no store. Create one on Printify and link it to your Etsy shop.");

    public static Error PrintifyProductNotFound() =>
        Error.NotFound("shop.printify_product_not_found", "This Printify product was not created from this APCS product.");

    public static Error PrintifyUnavailable() =>
        Error.Failure("shop.printify_unavailable", "Printify is not answering right now. Try again in a moment.");

    public static Error PrintifyRejected(Abstractions.Commerce.PrintifyFailureKind kind, string? reason) => kind switch
    {
        Abstractions.Commerce.PrintifyFailureKind.RateLimited => Error.Failure("shop.printify_rate_limited", "Printify is limiting requests. Try again in a minute."),
        Abstractions.Commerce.PrintifyFailureKind.Unauthorized => PrintifyKeyInvalid(),
        Abstractions.Commerce.PrintifyFailureKind.Rejected when !string.IsNullOrWhiteSpace(reason) => Error.Failure("shop.printify_rejected", $"Printify did not accept it: {reason}"),
        _ => PrintifyUnavailable(),
    };
}

/// <summary>Outcomes of the Etsy consent round trip, passed to the client page in the return address.</summary>
public static class EtsyConnectionOutcomes
{
    public const string Connected = "connected";
    /// <summary>The seller declined on Etsy's consent page.</summary>
    public const string Denied = "denied";
    /// <summary>The state was unknown or older than the allowed window, or already used.</summary>
    public const string Expired = "expired";
    /// <summary>The Etsy account has no shop to connect.</summary>
    public const string NoShop = "no_shop";
    /// <summary>
    /// Another APCS account already connected this Etsy shop. One shop answers to one account: Etsy reads a shop run
    /// from several accounts as possible fraud.
    /// </summary>
    public const string ShopTaken = "shop_taken";
    public const string Failed = "failed";
}

/// <summary>Connection statuses reported to the client.</summary>
public static class ShopConnectionStatuses
{
    public const string NotConnected = "not_connected";
    public const string Connected = "connected";
    public const string ReconnectRequired = "reconnect_required";
    public const string Unavailable = "unavailable";
}
