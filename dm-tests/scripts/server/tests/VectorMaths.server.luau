function Entry()
	assert(Vector2.One + Vector2.One == Vector2.One * 2)
	assert(Vector3.One + Vector3.One == Vector3.One * 2)

	assert(Vector2.One - Vector2.One == Vector2.Zero)
	assert(Vector3.One - Vector3.One == Vector3.Zero)

	assert(Color.FromHSV(0.5, 0.3, 1) == Color.FromHSV(0.5, 0.3, 1))
	Color.FromHSV(0.2, 0.4, 1, 0.5)

	assert(Color.FromHex("#F1F1F1") == Color.FromHex("#F1F1F1"))
end
