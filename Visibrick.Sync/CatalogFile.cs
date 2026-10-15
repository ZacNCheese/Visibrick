using Npgsql;
using NpgsqlTypes;
using Visibrick.Core.Models;
using Visibrick.Core.Rebrickable;

namespace Visibrick.Sync;

// One Rebrickable CSV dump and the Postgres table it fills
public abstract class CatalogFile
{
    public abstract string Name { get; }
    public abstract string Url { get; }
    public abstract string Table { get; }

    // Column list for COPY, in the same order WriteRowAsync writes them
    public abstract string Columns { get; }

    // Parses one CSV row and writes it to the COPY stream. Returns false to skip the row.
    public abstract Task<bool> WriteRowAsync(NpgsqlBinaryImporter importer, List<string> cols);

    protected static Task WriteTextAsync(NpgsqlBinaryImporter importer, string? value)
        => value is null
            ? importer.WriteNullAsync()
            : importer.WriteAsync(value, NpgsqlDbType.Text);
}

public class ColorsFile : CatalogFile
{
    public override string Name => "colors";
    public override string Url => RebrickableCsv.ColorsUrl;
    public override string Table => "lego_colors";
    public override string Columns => "id, name, rgb, is_translucent";

    public override async Task<bool> WriteRowAsync(NpgsqlBinaryImporter importer, List<string> cols)
    {
        if (RebrickableCsv.ParseColor(cols) is not LegoColor color)
            return false;

        await importer.StartRowAsync();
        await importer.WriteAsync(color.Id, NpgsqlDbType.Integer);
        await WriteTextAsync(importer, color.Name);
        await WriteTextAsync(importer, color.Rgb);
        await importer.WriteAsync(color.IsTranslucent, NpgsqlDbType.Boolean);
        return true;
    }
}

public class MinifigsFile : CatalogFile
{
    public override string Name => "minifigs";
    public override string Url => RebrickableCsv.MinifigsUrl;
    public override string Table => "lego_minifigs";
    public override string Columns => "number, name, num_parts, image_url";

    public override async Task<bool> WriteRowAsync(NpgsqlBinaryImporter importer, List<string> cols)
    {
        if (RebrickableCsv.ParseMinifig(cols) is not LegoMinifig minifig)
            return false;

        await importer.StartRowAsync();
        await WriteTextAsync(importer, minifig.Number);
        await WriteTextAsync(importer, minifig.Name);
        await importer.WriteAsync(minifig.NumParts, NpgsqlDbType.Integer);
        await WriteTextAsync(importer, minifig.ImageUrl);
        return true;
    }
}
