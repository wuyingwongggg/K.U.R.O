using Godot;

namespace Kuros.Core
{
    /// <summary>
    /// 可被投掷物拦截的屏障：实现者会被投掷物命中（伤害 + 停止，方向性屏障按方向判定）。
    /// 未实现者（其他投掷物/可拾取物/普通静态体/同伴等）投掷物直接穿过——
    /// 显式接口替代 HasMethod("TakeDamage") 鸭子类型，编译期保证接收者身份。
    /// </summary>
    public interface IBarrier
    {
        void TakeDamage(float damage);
    }
}
