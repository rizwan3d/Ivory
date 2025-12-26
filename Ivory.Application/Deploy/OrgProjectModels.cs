namespace Ivory.Application.Deploy;

public sealed class OrgSummary
{
    public Guid OrgId { get; set; }
    public string OrgName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public sealed class ProjectSummary
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid OrgId { get; set; }
    public string OrgName { get; set; } = string.Empty;
}
