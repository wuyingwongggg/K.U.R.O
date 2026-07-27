using Godot;
using Kuros.Items.World;

/// <summary>
/// 挂在 FurnitureDrone 的 AttackArea 子节点上。
/// 检测到 Boss 敌人的 StunArea 时，触发父级 RigidBodyWorldItemEntity 销毁。
/// </summary>
public partial class DroneContactHandler : Node
{
    private Area2D? _hitbox;
    private RigidBodyWorldItemEntity? _parentEntity;

    public override void _Ready()
    {
        _hitbox = GetParentOrNull<Area2D>();
        if (_hitbox != null)
            _hitbox.AreaEntered += OnAreaEntered;

        _parentEntity = FindParentEntity();
    }

    private RigidBodyWorldItemEntity? FindParentEntity()
    {
        Node? current = GetParent();
        while (current != null)
        {
            if (current is RigidBodyWorldItemEntity entity)
                return entity;
            current = current.GetParent();
        }
        return null;
    }

    private void OnAreaEntered(Area2D area)
    {
        if (area.Name != "StunArea") return;
        _parentEntity?.TriggerDestruction();
    }
}
