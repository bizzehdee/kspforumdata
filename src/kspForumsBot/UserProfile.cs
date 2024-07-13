using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace kspForumsBot
{
    internal class UserProfile
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string ProfileImage { get; set; }
        public string Group { get; set; }
        public string JoinedDateTime { get; set; }
        public int Reputation { get; set; }
        public string ReputationDesc { get; set; }
    }
}
