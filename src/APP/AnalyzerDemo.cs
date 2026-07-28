namespace APP
{
    // ===== AC001 示例：类实现了接口但缺少 [AutoInterface] =====
    // 构建时会看到警告：
    // warning AC001: 类 'DemoService' 实现了接口 'IDemoService' 但未标记 [AutoInterface]，建议添加该特性以自动生成接口
    public interface IDemoService
    {
        string GetData();
    }

    public class DemoService : IDemoService  // ← 缺少 [AutoInterface]，触发 AC001
    {
        public string GetData() => "data";
    }


    // ===== AC003 示例：[AutoIgnore] 标记在非公共成员上 =====
    // 构建时会看到警告：
    // warning AC003: 成员 'Helper' 不是公共成员，[AutoIgnore] 标记无意义，建议移除
    [AutoCode.Model.InterfaceAttribute.AutoInterface]
    public class IgnoreDemo : IIgnoreDemo
    {
        public int GetValue() => 1;

        [AutoCode.Model.InterfaceAttribute.AutoIgnore]
        private void Helper() { }  // ← private 方法上的 [AutoIgnore] 无意义，触发 AC003
    }

    public interface IIgnoreDemo
    {
        int GetValue();
    }
}
