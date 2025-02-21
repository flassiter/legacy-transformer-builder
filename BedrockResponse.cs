using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace legacy_transformer_builder
{
    public class Content
    {
        public string? Type { get; set; }
        public string? Text { get; set; }
    }

    public class BedrockResponse
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? Role { get; set; }
        public string? Model { get; set; }
        public List<Content>? Content { get; set; }
        public string? StopReason { get; set; }
        public object? StopSequence { get; set; }
        public Usage? Usage { get; set; }
    }
}
