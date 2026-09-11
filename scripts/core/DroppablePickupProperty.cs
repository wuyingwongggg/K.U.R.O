using Godot;

namespace Kuros.Core
{
	/// <summary>
	/// 可丢弃的拾取属性基类 - 可以被拾取和丢弃的物品
	/// </summary>
	public abstract partial class DroppablePickupProperty : PickupProperty
	{
		[Export] public Vector2 DropWorldOffset { get; set; } = Vector2.Zero;

		/// <summary>
		/// 当被放下时调用
		/// </summary>
		protected virtual void OnPutDown(GameActor actor)
		{
		}

		/// <summary>
		/// 获取丢弃物品的父节点（通常是场景根节点或特定的容器）
		/// </summary>
		protected Node2D GetDropParent(GameActor actor)
		{
		var currentScene = GetTree().CurrentScene;
			if (currentScene is Node2D sceneNode2D)
				return sceneNode2D;

			return this is Node2D ? (Node2D)this : actor;
		}
	}
}

