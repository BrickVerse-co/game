function Entry()
	local target = script.ScriptCallTarget

	assert(target.DaBall == "Ye")
	assert(target.a == nil)
	target.DaBall = 67
	assert(target.DaBall == 67)
	target.TestTarget()
end
