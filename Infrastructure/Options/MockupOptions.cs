using APCS.Common.Constants;

namespace APCS.Infrastructure.Options;

/// <summary>Settings for processing mock-up base photos.</summary>
public sealed class MockupOptions
{
    public const string SectionName = ConfigurationSections.Mockups;

    /// <summary>
    /// The BiRefNet (lite) ONNX model, in an export made for CPUs, that finds the product in a
    /// photo. A relative path is looked up from the content root and then from its parent (the
    /// solution folder). The file is not in the repository (it is larger than a Git host accepts);
    /// it is downloaded on startup when missing (or by hand with
    /// <c>scripts/Get-SegmentationModel.ps1</c>). Without it, mock-up photos cannot be processed.
    /// </summary>
    public string SegmentationModelPath { get; set; } = "models/birefnet-lite-cpu.onnx";

    /// <summary>Where a missing model is downloaded from on startup. Empty turns the download off.</summary>
    public string SegmentationModelUrl { get; set; } =
        "https://huggingface.co/senty-au/BiRefNet_lite-ONNX-dynamic/resolve/main/onnx/model.onnx";

    /// <summary>The SHA-256 the downloaded file must have to be installed.</summary>
    public string SegmentationModelSha256 { get; set; } = "1e0da42f0fde010e32e938bad388457ecefe35806fde9d923421997861ae9391";
}
