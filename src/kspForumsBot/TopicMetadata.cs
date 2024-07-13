using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace kspForumsBot
{
    internal class TopicMetadata
    {
        public int TopicId { get; set; }
        public int ForumId { get; set; }
        public string TopicTitle { get; set; }
        public string TopicUrl { get; set; }
        public int PageCount { get; set; }
        public string CreatedByName { get; set; }
        public int CreatedById { get; set; }
        public string CreatedDateTime { get; set; }
    }
}
