namespace APCS.Application.Abstractions.AI;

/// <summary>Reads the lettering in a generated design image (OCR), so a misspelt or extra word can be shown to the Seller.</summary>
public interface IDesignTextReader
{
    /// <summary>
    /// The lines of text found in the image, or null when reading is turned off or failed: a design is never held back
    /// because its text could not be read.
    /// </summary>
    Task<IReadOnlyList<DesignTextLine>?> ReadAsync(byte[] image, CancellationToken cancellationToken = default);
}

/// <summary>One line of text the OCR found, with how sure it is (0 to 1).</summary>
public sealed record DesignTextLine(string Text, double Score);
