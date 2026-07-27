using AutoCode.Model.InterfaceAttribute;


namespace APP
{
    [AutoInterface]
    public class AutoInterface : IAutoInterface
    {
        [AutoIgnore]
        public int GetId() => 1;

        public int GetName() => 1;

        public string GetName3(string name,int age) => name;
    }


    [AutoInterface]
    public class AutoInterfaceV : IAutoInterfaceV
    {
        public int GetId() => 1;
    }


}
