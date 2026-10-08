using DTIOneLink.Services;
using Microsoft.AspNetCore.Http;

namespace DTIOneLink.Tests;

public class ProofFileValidatorTests
{
    private static IFormFile MakeFile(byte[] content, string fileName, long? lengthOverride = null)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, lengthOverride ?? content.Length, "file", fileName);
    }

    private static readonly byte[] PdfHeader = { 0x25, 0x50, 0x44, 0x46, 0x00, 0x00, 0x00, 0x00 };
    private static readonly byte[] JpgHeader = { 0xFF, 0xD8, 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00 };
    private static readonly byte[] PngHeader = { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x00, 0x00, 0x00 };
    private static readonly byte[] ZipHeader = { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00 }; // docx/xlsx

    [Fact]
    public void Validate_EmptyFile_IsRejected()
    {
        var (isValid, error) = ProofFileValidator.Validate(MakeFile(Array.Empty<byte>(), "proof.pdf", lengthOverride: 0));

        Assert.False(isValid);
        Assert.Equal("The uploaded file is empty.", error);
    }

    [Fact]
    public void Validate_OverTheSizeLimit_IsRejected()
    {
        var file = MakeFile(PdfHeader, "proof.pdf", lengthOverride: ProofFileValidator.MaxFileSizeBytes + 1);

        var (isValid, error) = ProofFileValidator.Validate(file);

        Assert.False(isValid);
        Assert.Equal("File must be 10 MB or smaller.", error);
    }

    [Theory]
    [InlineData("proof.exe")]
    [InlineData("proof.html")]
    [InlineData("proof")] // no extension at all
    public void Validate_DisallowedExtension_IsRejected(string fileName)
    {
        var (isValid, error) = ProofFileValidator.Validate(MakeFile(PdfHeader, fileName));

        Assert.False(isValid);
        Assert.Equal("Only PDF, DOCX, XLSX, JPG, and PNG files are allowed.", error);
    }

    [Fact]
    public void Validate_AllowedExtensionButWrongMagicBytes_IsRejected()
    {
        // A renamed .exe/.html given a .pdf extension: the extension passes,
        // but the file's real contents don't match the PDF signature.
        var file = MakeFile(JpgHeader, "proof.pdf");

        var (isValid, error) = ProofFileValidator.Validate(file);

        Assert.False(isValid);
        Assert.Equal("The file's contents don't match its extension. Please re-upload a genuine file of that type.", error);
    }

    [Theory]
    [MemberData(nameof(ValidFiles))]
    public void Validate_GenuineAllowedFile_IsAccepted(byte[] header, string fileName)
    {
        var (isValid, error) = ProofFileValidator.Validate(MakeFile(header, fileName));

        Assert.True(isValid);
        Assert.Null(error);
    }

    public static IEnumerable<object[]> ValidFiles()
    {
        yield return new object[] { PdfHeader, "proof.pdf" };
        yield return new object[] { JpgHeader, "proof.jpg" };
        yield return new object[] { JpgHeader, "proof.jpeg" };
        yield return new object[] { PngHeader, "proof.png" };
        yield return new object[] { ZipHeader, "proof.docx" };
        yield return new object[] { ZipHeader, "proof.xlsx" };
    }

    [Fact]
    public void Validate_LeavesTheStreamPositionedAtStart_SoTheCallerCanStillReadIt()
    {
        var file = MakeFile(PdfHeader, "proof.pdf");

        ProofFileValidator.Validate(file);

        using var stream = file.OpenReadStream();
        Assert.Equal(0, stream.Position);
    }
}
