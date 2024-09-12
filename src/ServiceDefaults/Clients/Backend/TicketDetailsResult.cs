namespace eShopSupport.ServiceDefaults.Clients.Backend;

public class TicketDetailsResult
{
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CustomerFullName { get; set; }
    public int CustomerId { get; set; }
    public int? CustomerSatisfaction { get; set; }
    public string? LongSummary { get; set; }
    public ICollection<TicketDetailsResultMessage> Messages { get; set; }
    public string? ProductBrand { get; set; }
    public string? ProductDescription { get; set; }
    public int? ProductId { get; set; }
    public string? ProductModel { get; set; }
    public string? ShortSummary { get; set; }
    public int TicketId { get; set; }
    public TicketStatus TicketStatus { get; set; }
    public TicketType TicketType { get; set; }

    public TicketDetailsResult()
    {
        Messages = [];
    }
}
