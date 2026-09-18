extends SceneTree

func _init():
	# 模拟 StageGeneratorManager 的顺序：先 AddChild（_Ready 跑）→ 再摆位
	var room = load("res://scenes/levels/F_begin.tscn").instantiate()
	get_root().add_child(room)
	room.position = Vector2(2755, 0)
	await process_frame

	var sign = room.get_node("Environments/exit_sign")
	var ma = room.get_node("Environments/Marker2D")
	var mb = room.get_node("Environments/Marker2D2")
	print("[V] Marker 世界X = ", ma.global_position.x, " / ", mb.global_position.x, "（区间宽 ", mb.global_position.x - ma.global_position.x, "）")

	var p := Node2D.new()
	p.name = "FakePlayer"
	p.add_to_group("player")
	get_root().add_child(p)

	for x in [505.0, 4000.0, 6000.0]:
		p.global_position = Vector2(x, -660)
		for i in range(150):
			await process_frame
		print("[V] 玩家世界X=", x, " -> sign世界X=", snappedf(sign.global_position.x, 0.01))

	room.queue_free()
	await process_frame
	quit()
