using AutoCode.Model.InterfaceAttribute;

namespace APP
{
    // ===== AC001 示例：类实现了接口但缺少 [AutoInterface] =====
    // 构建时会看到警告：
    // warning AC001: 类 'DemoService' 实现了接口 'IDemoService' 但未标记 [AutoInterface]
    // IDE 中 Ctrl+. → "添加 [AutoInterface] 特性"
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
    // warning AC003: 成员 'Helper' 不是公共成员，[AutoIgnore] 标记无意义
    // IDE 中 Ctrl+. → "移除无意义的 [AutoIgnore]"
    [AutoInterface]
    public class IgnoreDemo : IIgnoreDemo  // IIgnoreDemo 由生成器自动创建
    {
        public int GetValue() => 1;

        [AutoIgnore]
        private void Helper() { }  // ← private 方法上的 [AutoIgnore] 无意义，触发 AC003
    }
}
