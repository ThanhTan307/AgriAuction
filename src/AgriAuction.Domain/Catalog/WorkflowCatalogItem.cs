namespace AgriAuction.Domain.Catalog;
public sealed class WorkflowCatalogItem
{
    public Guid Id { get; set; }=Guid.NewGuid();
    public string Kind { get; set; }="";
    public string Name { get; set; }="";
    public bool Active { get; set; }=true;
    public long Version { get; set; }=1;
}
