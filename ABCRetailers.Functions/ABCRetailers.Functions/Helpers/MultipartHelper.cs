using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using System.Text;

namespace ABCRetailers.Functions.Helpers;
public static class MultipartHelper
{
    public sealed record FilePart(string FieldName, string FileName, Stream Data);
    public sealed record FormData(IReadOnlyDictionary<string, string> Text, IReadOnlyList<FilePart> Files);

    public static async Task<FormData> ParseAsync(Stream body, string contentType)
    {
        var boundary = HeaderUtilities.RemoveQuotes(MediaTypeHeaderValue.Parse(contentType).Boundary).Value
                       ?? throw new InvalidOperationException("Multipart boundary missing");

        var reader = new MultipartReader(boundary, body);
        var text = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<FilePart>();

        for (var section = await reader.ReadNextSectionAsync(); section != null; section = await reader.ReadNextSectionAsync())
        {
            var cd = ContentDispositionHeaderValue.Parse(section.ContentDisposition);
            if (cd.IsFileDisposition())
            {
                var fieldName = cd.Name.Value?.Trim('"') ?? "file";
                var fileName = cd.FileName.Value?.Trim('"') ?? "upload.bin";
                var ms = new MemoryStream();
                await section.Body.CopyToAsync(ms);
                ms.Position = 0;
                files.Add(new FilePart(fieldName, fileName, ms));
            }
            else if (cd.IsFormDisposition())
            {
                var fieldName = cd.Name.Value?.Trim('"') ?? "";
                using var sr = new StreamReader(section.Body, Encoding.UTF8);
                text[fieldName] = await sr.ReadToEndAsync();
            }
        }
        return new FormData(text, files);
    }
}
