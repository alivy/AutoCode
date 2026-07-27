// See https://aka.ms/new-console-template for more information
using APP.Map;
using System.Collections.Generic;

Console.WriteLine("Hello, World!");

var source = new UserInfo
{
    Id = 1,
    address = new List<Address>
            {
                new Address { City = "City1", State = "State1" },
                new Address { City = "City2", State = "State2" }
            }
};

var target = new UserInfo();

source.CopyTo(target);

Console.WriteLine(target.Id); // 1
Console.WriteLine(target.address[0].City); // City1
