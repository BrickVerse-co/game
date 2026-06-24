function Entry()
	assert(Vector2.One * 2 == Vector2.New(2, 2))
	assert(Vector2.New(1, 1) * 2 == Vector2.New(2, 2))
	assert(Vector3.One * 2 == Vector3.New(2, 2, 2))
	assert(Vector3.New(1, 1, 1) * 2 == Vector3.New(2, 2, 2))
end
