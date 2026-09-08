namespace Kuros.Systems
{
    /// <summary>
    /// 一次构筑卡选择的记录(时间序)。被反向卡取代的卡置 Active=false 保留于此——
    /// 废弃记录仅供 UI 层叠展示(灰+废标),不参与数值、池过滤与层数聚合。
    /// </summary>
    public sealed class BuildPickRecord
    {
        public string EffectId = string.Empty;
        public int Stacks;
        public bool Active;
    }
}
