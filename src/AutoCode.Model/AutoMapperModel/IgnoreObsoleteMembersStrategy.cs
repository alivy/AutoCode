using System;

namespace AutoCode.Model.AutoMapperModel
{
    /// <summary>
    /// 定义映射标记为的成员时使用的策略 <see cref="ObsoleteAttribute"/>.
    /// 请注意，<see cref="MapPropertyAttribute"/> 将始终映射 <see cref="ObsoleteAttribute"/> -标记的成员，
    /// even if they are ignored.
    /// </summary>
    [Flags]
    public enum IgnoreObsoleteMembersStrategy
    {
        /// <summary>
        /// Maps <see cref="ObsoleteAttribute"/> marked members.
        /// </summary>
        None = 0,

        /// <summary>
        /// Will not map <see cref="ObsoleteAttribute"/> marked source or target members.
        /// </summary>
        Both = ~None,

        /// <summary>
        /// Ignores source <see cref="ObsoleteAttribute"/> marked members.
        /// </summary>
        Source = 1 << 0,

        /// <summary>
        /// Ignores target <see cref="ObsoleteAttribute"/> marked members.
        /// </summary>
        Target = 1 << 1,
    }
}
