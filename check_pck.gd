extends SceneTree

func _init():
	print("=== PCK Spine 资源检查 ===")
	var paths := [
		"res://animations/spine/Enemy_Normal_guard1.tres",
		"res://animations/spine/Enemy_A1_zhuA.tres",
		"res://assets/spine/demo_enemy/Enemy_Normal_guard1.spine-json",
		"res://assets/spine/demo_enemy/Enemy_Normal_guard1.atlas",
		"res://assets/spine/demo_enemy/Enemy_Normal_guard1.png",
	]
	for p in paths:
		print("EXISTS? ", p, " => ", ResourceLoader.exists(p))
		var res := ResourceLoader.load(p, "", ResourceLoader.CACHE_MODE_IGNORE)
		if res == null:
			print("  LOAD FAILED (null)")
		else:
			print("  LOAD OK: ", res.get_class())
	quit()
