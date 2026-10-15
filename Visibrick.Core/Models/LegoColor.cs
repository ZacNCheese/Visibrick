using SQLite;

namespace Visibrick.Models;

public class LegoColor
{
    [PrimaryKey]
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Rgb { get; set; }

    public bool IsTranslucent { get; set; }
}
