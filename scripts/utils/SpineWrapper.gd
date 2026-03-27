extends Node

# 这是一个 GDScript 桥接脚本，用于帮助 C# 操作 Spine 节点
# 因为 C# 绑定可能缺失，我们在 GDScript 中进行操作

func find_spine_node(root: Node) -> Node:
	if root.has_node("SpineCharacter"):
		return root.get_node("SpineCharacter")
	if root.has_node("SpineSprite"):
		return root.get_node("SpineSprite")
	# 尝试递归查找
	var found = root.find_child("SpineSprite", true, false)
	if found: return found
	return root.find_child("SpineCharacter", true, false)

func flip_facing(root: Node, face_right: bool, default_face_left: bool) -> void:
	var sprite = find_spine_node(root)
	if sprite:
		var sign_val = 1.0 if face_right else -1.0
		if default_face_left: sign_val *= -1.0
		sprite.scale.x = abs(sprite.scale.x) * sign_val

func flash_damage(root: Node, color: Color) -> void:
	var sprite = find_spine_node(root)
	if sprite:
		var original_modulate = sprite.modulate
		sprite.modulate = color
		
		var tween = create_tween()
		tween.tween_interval(0.1)
		tween.tween_callback(func(): 
			if is_instance_valid(sprite):
				sprite.modulate = original_modulate
		)

# 动画控制相关
func play_animation(root: Node, anim_name: String, loop: bool, mix_duration: float = 0.1, time_scale: float = 1.0) -> bool:
	var sprite = find_spine_node(root)
	if not sprite:
		print("[SpineWrapper] ERROR: 無法找到 SpineSprite 節點在: ", root)
		return false
	
	print("[SpineWrapper] 找到 SpineSprite: ", sprite)
	
	var state = sprite.get_animation_state()
	if not state:
		print("[SpineWrapper] ERROR: 無法獲得 AnimationState")
		return false
	
	print("[SpineWrapper] 嘗試播放動畫: ", anim_name, " (loop: ", loop, ")")
	var entry = state.set_animation(anim_name, loop)
	
	if entry:
		print("[SpineWrapper] 動畫播放成功，設置參數...")
		# Use setter methods instead of direct property assignment
		if entry.has_method("set_mix_duration"):
			entry.set_mix_duration(mix_duration)
		if entry.has_method("set_time_scale"):
			entry.set_time_scale(time_scale)
		return true
	else:
		print("[SpineWrapper] ERROR: set_animation() 返回 nil，動畫可能不存在或其他錯誤")
		# 嘗試列舉所有可用動畫
		var skeleton = sprite.get_skeleton()
		if skeleton and skeleton.has_method("get_animations"):
			var animations = skeleton.get_animations()
			print("[SpineWrapper] 可用的動畫列表: ", animations)
		return false

func add_animation(root: Node, anim_name: String, loop: bool, delay: float, mix_duration: float = 0.1, time_scale: float = 1.0) -> bool:
	var sprite = find_spine_node(root)
	if not sprite: return false
	
	var state = sprite.get_animation_state()
	if not state: return false
	
	var entry = state.add_animation(anim_name, loop, delay)
	if entry:
		# Use setter methods instead of direct property assignment
		if entry.has_method("set_mix_duration"):
			entry.set_mix_duration(mix_duration)
		if entry.has_method("set_time_scale"):
			entry.set_time_scale(time_scale)
	return true

func set_empty_animation(root: Node, track_index: int, mix_duration: float) -> bool:
	var sprite = find_spine_node(root)
	if not sprite: return false
	
	var state = sprite.get_animation_state()
	if not state: return false
	
	state.set_empty_animation(track_index, mix_duration)
	return true

func play_partial_loop_animation(root: Node, anim_name: String, loop_start: float, loop_end: float, mix_duration: float = 0.1, time_scale: float = 1.0) -> bool:
	var sprite = find_spine_node(root)
	if not sprite:
		return false

	if loop_end <= loop_start:
		return false

	var state = sprite.get_animation_state()
	if not state:
		return false

	# 先整段从头播到 loop_start，再由 update_partial_loop_animation 维持分段循环
	var entry = state.set_animation(anim_name, false)
	if not entry:
		return false

	if entry.has_method("set_mix_duration"):
		entry.set_mix_duration(mix_duration)
	if entry.has_method("set_time_scale"):
		entry.set_time_scale(time_scale)

	return true

func play_partial_once_animation(root: Node, anim_name: String, part_start: float, part_end: float, mix_duration: float = 0.1, time_scale: float = 1.0) -> bool:
	var sprite = find_spine_node(root)
	if not sprite:
		return false

	if part_end <= part_start:
		return false

	var state = sprite.get_animation_state()
	if not state:
		return false

	var entry = state.set_animation(anim_name, false)
	if not entry:
		return false

	if entry.has_method("set_mix_duration"):
		entry.set_mix_duration(mix_duration)
	if entry.has_method("set_time_scale"):
		entry.set_time_scale(time_scale)
	if entry.has_method("set_track_time"):
		entry.set_track_time(part_start)
	if entry.has_method("set_track_last"):
		entry.set_track_last(part_start)
	if entry.has_method("set_track_end"):
		entry.set_track_end(part_end)

	return true

func update_partial_loop_animation(root: Node, track_index: int, loop_start: float, loop_end: float) -> bool:
	var sprite = find_spine_node(root)
	if not sprite:
		return false

	if loop_end <= loop_start:
		return false

	var state = sprite.get_animation_state()
	if not state or not state.has_method("get_current"):
		return false

	var entry = state.get_current(track_index)
	if not entry:
		return false

	if not entry.has_method("get_track_time"):
		return false

	var track_time := float(entry.get_track_time())

	if track_time < loop_end:
		return true

	var loop_len = maxf(loop_end - loop_start, 0.0001)
	var wrapped_time = loop_start + fmod(maxf(track_time - loop_start, 0.0), loop_len)

	if not entry.has_method("set_track_time"):
		return false

	entry.set_track_time(wrapped_time)

	if entry.has_method("set_track_last"):
		entry.set_track_last(wrapped_time)

	return true
