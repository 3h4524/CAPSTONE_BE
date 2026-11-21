using System.Text.Json;
using APCS.Application.Features.ListingContent.Dtos;

namespace APCS.Application.Features.ListingStreams.Common;

public sealed record ListingStreamStartData(Guid ProductId, string Model);

public sealed record ListingStreamDeltaData(Guid ProductId, string Text);

public sealed record ListingStreamResetData(Guid ProductId);

public sealed record ListingStreamDoneData(Guid ProductId, ListingContentResponseDto Listing);

public sealed record ListingStreamFailedData(Guid ProductId, string Message);

public sealed record ListingStreamEndData();

public sealed record ListingStreamEvent(string Type, object Data)
{
    public const string StartType = "start";
    public const string DeltaType = "delta";
    public const string ResetType = "reset";
    public const string DoneType = "done";
    public const string FailedType = "failed";
    public const string EndType = "end";

    private static readonly JsonSerializerOptions DataJsonOptions = new(JsonSerializerDefaults.Web);

    public string DataJson() => JsonSerializer.Serialize(Data, Data.GetType(), DataJsonOptions);

    public static bool IsTerminal(string type) => type == EndType;

    public static ListingStreamEvent Start(Guid productId, string model) => new(StartType, new ListingStreamStartData(productId, model));

    public static ListingStreamEvent Delta(Guid productId, string text) => new(DeltaType, new ListingStreamDeltaData(productId, text));

    public static ListingStreamEvent Reset(Guid productId) => new(ResetType, new ListingStreamResetData(productId));

    public static ListingStreamEvent Done(Guid productId, ListingContentResponseDto listing) =>
        new(DoneType, new ListingStreamDoneData(productId, listing));

    public static ListingStreamEvent Failed(Guid productId, string message) => new(FailedType, new ListingStreamFailedData(productId, message));

    public static ListingStreamEvent End() => new(EndType, new ListingStreamEndData());
}
