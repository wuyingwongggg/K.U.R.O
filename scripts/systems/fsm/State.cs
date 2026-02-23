using Godot;
using System;
using Kuros.Core;

namespace Kuros.Systems.FSM
{
    public abstract partial class State : Node
    {
        protected GameActor Actor { get; private set; } = null!;
        protected StateMachine Machine { get; private set; } = null!;

        public void Initialize(GameActor actor, StateMachine machine)
        {
            Actor = actor;
            Machine = machine;
            _ReadyState();
        }

        // Optional override for initialization logic
        protected virtual void _ReadyState() { }

        public virtual void Enter() { }
        public virtual void Exit() { }
        
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

