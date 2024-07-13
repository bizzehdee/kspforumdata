using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace kspForumsBot
{
    public class ForumStructure
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Url { get; set; }
        public Collection<ForumStructure> Forums { get; set; } = new Collection<ForumStructure>();
    }
}
