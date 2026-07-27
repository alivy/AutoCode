using Auto.MapModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace APP.Map
{
    public class UserInfo
    {
        public int Id { get; set; }

        public People people { get; set; }

        public List<Address> address { get; set; }

        public List<UserBehavior> UserBehaviors { get; set; }
        //public Dictionary<int, UserBehavior> userBehaviorDic { get; set; }
    }

    public class Address
    {
        public string City { get; set; }

        public string State { get; set; }

    }

    public class People
    {
        public string Name { get; set; }
        public string Age { get; set; }
    }
}
