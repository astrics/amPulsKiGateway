namespace AiGateway.Api.Models
{
    public class StatementResultEntry
    {
        public string StatementId { get; set; } = "";
        public string MetadatenId { get; set; } = "";
        public string Dashboard { get; set; } = "";
        public string Text { get; set; } = "";
        public string Sentiment { get; set; } = "";
        public List<KeywordEntry> Keywords { get; set; } = new();
        public long ProcessingMs { get; set; }
        public DateTime ProcessedAt { get; set; }
        public string? Error { get; set; }
        public string? RawResponse { get; set; }
    }
}
