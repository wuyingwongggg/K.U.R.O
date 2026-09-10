using Godot;
using System.Collections.Generic;
using Kuros.Core;

namespace Kuros.Core.Effects
{
    /// <summary>
    /// 管理角色身上的所有效果，负责生命周期更新。
    /// </summary>
    public partial class EffectController : Node
    {
        private readonly List<ActorEffect> _effects = new();
        private GameActor? _actor = null;

        public override void _Ready()
        {
            _actor = GetParent<GameActor>();
            if (_actor == null)
            {
                GD.PushError("EffectController must be a child of GameActor.");
                QueueFree();
            }
        }

        public override void _Process(double delta)
        {
            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                _effects[i].Tick(delta);
            }
        }

        public override void _ExitTree()
        {
            // 节点销毁（场景卸载/角色销毁）时清理效果：保证 ActorEffect.OnRemoved 执行（恢复属性/清除注入委托），
            // 否则效果修改的数值与委托残留到下次战斗
            ClearAll();
            base._ExitTree();
        }

        public ActorEffect? GetEffect(string effectId)
        {
            return _effects.Find(effect => effect.EffectId == effectId);
        }

        public T? GetEffect<T>() where T : ActorEffect
        {
            return _effects.Find(e => e is T) as T;
        }

        /// <summary>按接口查找效果(供 items 等下层组件查询玩家身上的构筑能力,避免依赖具体卡类型)。</summary>
        public T? GetEffectByInterface<T>() where T : class
        {
            foreach (var effect in _effects)
            {
                if (effect is T match) return match;
            }
            return null;
        }

        /// <summary>遍历所有实现接口 T 的效果(供多来源聚合查询,如玩家投掷修饰逐卡叠加)。</summary>
        public void ForEachEffect<T>(System.Action<T> action) where T : class
        {
            foreach (var effect in _effects)
            {
                if (effect is T match) action(match);
            }
        }

        public void AddEffect(ActorEffect effect)
        {
            if (effect == null) return;

            if (_actor == null) return;

            var existing = GetEffect(effect.EffectId);
            if (existing != null)
            {
                existing.Refresh();
                return;
            }

            AddChild(effect);
            _effects.Add(effect);
            effect.Initialize(_actor, this);
        }

        public ActorEffect? AddEffectFromScene(PackedScene? effectScene)
        {
            if (effectScene == null)
            {
                return null;
            }

            var effectInstance = effectScene.Instantiate<ActorEffect>();
            if (effectInstance == null)
            {
                GD.PushWarning($"Failed to instantiate effect scene {effectScene.ResourcePath}");
                return null;
            }

            AddEffect(effectInstance);
            return effectInstance;
        }

        public void RemoveEffect(ActorEffect effect)
        {
            if (!_effects.Remove(effect)) return;
            effect.OnRemoved();
            effect.QueueFree();
        }

        public void ClearAll()
        {
            foreach (var effect in _effects)
            {
                effect.OnRemoved();
                effect.QueueFree();
            }

            _effects.Clear();
        }
    }
}

