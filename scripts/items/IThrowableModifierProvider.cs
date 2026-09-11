namespace Kuros.Items
{
    /// <summary>
    /// 构筑修饰提供方(单一 seam)：持有构筑的 actor(如玩家)实现此接口，
    /// 投掷/预览时把当前构筑对投掷家具的修饰带给解析层。现无人实现 = 全 None 原行为；
    /// 将来 build 系统在玩家类上实现即可，消费点零改动。
    /// </summary>
    public interface IThrowableModifierProvider
    {
        ThrowableModifiers GetThrowableModifiers();
    }
}
