using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace legacy_transformer_builder
{
    public class Usage
    {
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
    }

    public class Metadata
    {
        public string? ObjectName { get; set; }
        public string? ObjectType { get; set; }
        public string? ObjectAttribute { get; set; }
        public string? ObjectFamily { get; set; }
        public string? ObjectDescription { get; set; }
        public DateTime ObjectFirstDefined { get; set; }
        public DateTime ObjectLastTouched { get; set; }
        public int ObjectDependencyCount { get; set; }
        public int ObjectReferencedByCount { get; set; }
    }

    public class AnalysisRequest
    {
        public required Metadata Metadata { get; set; }
        public required string SourceCode { get; set; }
        public required string EnterpriseDomainsJSON { get; set; }
    }
}
