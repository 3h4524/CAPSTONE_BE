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

    /// <summary>
    /// The SegFormer B2 clothes model (ONNX, quantized), which finds the garment on a photo of a person so a model
    /// photo needs no garment selected by hand. Looked up and downloaded like <see cref="SegmentationModelPath"/>;
    /// without it the garment is selected by hand. Its weights are for non-commercial use only.
    /// </summary>
    public string ClothesModelPath { get; set; } = "models/segformer-b2-clothes-quantized.onnx";

    /// <summary>Where a missing clothes model is downloaded from on startup. Empty turns the download off.</summary>
    public string ClothesModelUrl { get; set; } =
        "https://huggingface.co/Xenova/segformer_b2_clothes/resolve/main/onnx/model_quantized.onnx";

    /// <summary>The SHA-256 the downloaded clothes model must have to be installed.</summary>
    public string ClothesModelSha256 { get; set; } = "2b18fbdd196a7ee12702c2fceaa6f812fe70d1154573a43bd62667b9157ee48d";
}
