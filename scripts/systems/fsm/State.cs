using Godot;
using System;
using Kuros.Core;

namespace Kuros.Systems.FSM
{
    public abstract partial class State : Node
    {
        /// <summary>被控制的 GameActor（玩家/敌人使用；P2 等非 GameActor 时为 null，用 Owner）。</summary>
        protected GameActor Actor { get; private set; } = null!;
        /// <summary>被控制的节点（GameActor 或 P2 等自定义节点）。</summary>
        protected Node Owner { get; private set; } = null!;
        protected StateMachine Machine { get; private set; } = null!;

        public void Initialize(Node owner, StateMachine machine)
        {
            Owner = owner;
            Actor = owner as GameActor;
            Machine = machine;
            _ReadyState();
        }

        // Optional override for initialization logic
        protected virtual void _ReadyState() { }

        public virtual void Enter() { }
        public virtual void Exit() { }

        // 这是可选的转换。状态可以重写以阻止中断。
        public virtual bool CanExitTo(string nextStateName) => true;
        public virtual bool CanEnterFrom(string? currentStateName) => true;
        
        public virtual void Update(double delta) { }
        public virtual void PhysicsUpdate(double delta) { }
        public virtual void HandleInput(InputEvent @event) { }
        
        // Utility to change state easily from within a state
        protected void ChangeState(string stateName)
        {
            Machine.ChangeState(stateName);
        }
    }
}

