function Entry()
	local p = Part.New(Environment)
	p:Destroy()

	-- Deleted reference should still matches
	assert(p == p, "deleted reference does not match!")
end
