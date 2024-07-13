using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace kerbalScrape
{
    internal class TopicArticlePage
    {
        public int TopicId { get; set; }
        public int ForumId { get; set; }
        public string TopicTitle { get; set; }
        public string CreatedByName { get; set; }
        public int CreatedById { get; set; }
        public string CreatedDateTime { get; set; }
        public int PageNum { get; set; }
        public List<TopicArticle> Articles { get; set; } = new List<TopicArticle>();
    }
}
