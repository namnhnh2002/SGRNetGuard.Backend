namespace SGRNetGuard.Api.Models;

public sealed class ITSupportDto
{
    public int Id { get; set; }
    public string? Site { get; set; }
    public string Region { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? TeamsUrl { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class ITSupportUpsertRequest
{
    public string Site { get; set; } = "";
    public string Region { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? TeamsUrl { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}