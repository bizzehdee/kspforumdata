using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace kerbalScrape
{
    internal class TopicArticle
    {
        public string CreatedByName { get; set; }
        public int CreatedById { get; set; }
        public string CreatedDateTime { get; set; }
        public string Content { get; set; }
    }
}
