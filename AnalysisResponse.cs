using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace legacy_transformer_builder
{
    public class AnalysisResponse
    {
        
        public string ObjectName { get; set; }

        
        public string ObjectType { get; set; }

        
        public string LevelOneDomain { get; set; }

        
        public string LevelTwoDomain { get; set; }

        
        public Documentation Documentation { get; set; }
    }

    public class Documentation
    {
       
        public string ProgramName { get; set; }

       
        public string Description { get; set; }

        public string CreationDate { get; set; }

      
        public string Author { get; set; }

     
        public List<Action> Actions { get; set; }

     
        public List<Parameter> Parameters { get; set; }

     
        public List<Message> Messages { get; set; }
    }

    public class Action
    {
       
        public string Type { get; set; }

       
        public string Description { get; set; }
    }

    public class Parameter
    {
      
        public string Name { get; set; }

       
        public string Type { get; set; }

       
        public string Description { get; set; }

        
        public string DefaultValue { get; set; }
    }

    public class Message
    {
       
        public string MessageId { get; set; }

      
        public string Description { get; set; }
    }

}
