extends Node

@export var sprite_path: NodePath = "../SpineSprite"
@export var is_spine: bool = true
@export_range(0.01, 0.5, 0.01) var interval: float = 0.05
@export_range(0.1, 2.0, 0.05) var lifetime: float = 0.3
@export_range(0.0, 1.0, 0.05) var start_alpha: float = 0.5
@export var ghost_color: Color = Color.WHITE

var _source: Node2D
var _timer: float = 0.0
var _active: bool = false
var _ghosts: Array = []

func _ready():
	_source = get_node_or_null(sprite_path)

func start():
	_active = true
	_timer = 0.0

func stop():
	_active = false
	_clear_ghosts()

func _physics_process(delta):
	if not _active or _source == null:
		return
	_timer -= delta
	if _timer <= 0.0:
		_timer = interval
		_spawn_ghost()

func _spawn_ghost():
	var enemy = get_parent()
	var world = enemy.get_parent()
	if world == null:
		return

	# 捕获当前动画状态（仅 Spine）
	var current_anim := ""
	var track_time := 0.0
	if is_spine:
		var anim_state = _source.get_animation_state()
		if anim_state:
			var entry = anim_state.get_current(0)
			if entry:
				var anim = entry.get_animation()
				if anim:
					current_anim = anim.get_name()
				track_time = entry.get_track_time()

	var ghost = _source.duplicate()
	ghost.top_level = true
	ghost.global_position = _source.global_position
	ghost.scale = enemy.scale * _source.scale
	ghost.modulate = Color(
		ghost_color.r, ghost_color.g, ghost_color.b,
		ghost_color.a * start_alpha
	)
	ghost.z_index = enemy.z_index - 1

	world.add_child(ghost)

	# 在 ghost 上恢复动画并冻结（仅 Spine）
	if is_spine and current_anim != "":
		ghost.play(current_anim, true, 0.0, 0.0)
		var ghost_state = ghost.get_animation_state()
		if ghost_state:
			var ghost_entry = ghost_state.get_current(0)
			if ghost_entry:
				ghost_entry.set_track_time(track_time)

	_ghosts.append(ghost)

	var end_color = ghost.modulate
	end_color.a = 0.0
	var tween = ghost.create_tween()
	tween.tween_property(ghost, "modulate", end_color, lifetime)
	tween.tween_callback(_on_ghost_finished.bind(ghost))

func _on_ghost_finished(ghost: Node2D):
	_ghosts.erase(ghost)
	if is_instance_valid(ghost):
		ghost.queue_free()

func _clear_ghosts():
	for ghost in _ghosts:
		if is_instance_valid(ghost):
			ghost.queue_free()
	_ghosts.clear()

func _exit_tree():
	_clear_ghosts()
