function Entry()
	local suite: Dynamic = Environment.RaycastSuite
	local p1: Part = suite["1"]
	local p2: Part = suite["2"]
	local p3: Part = suite["3"]
	local npc: NPC = suite["NPC"]

	local hit = Environment:Raycast(p1.Position, p1.Forward, 10, {})
	assert(hit.Instance == npc)

	local hit2 = Environment:Raycast(p1.Position, p1.Forward, 10, { npc })
	assert(hit2.Instance == p2)

	local hit3 = Environment:Raycast(p1.Position, p1.Forward, 10, { p2, npc })
	assert(hit3.Instance == p3)

	local hit4 = Environment:Raycast(p1.Position, p1.Forward, 10, { p2, npc, p3 })
	assert(hit4 == nil or hit3["Instance"] == nil)
end
