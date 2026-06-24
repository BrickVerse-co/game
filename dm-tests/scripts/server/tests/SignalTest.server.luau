function Entry()
	local testValue: IntValue = Instance.New("IntValue")
	testValue.Value = 0

	local pass = false
	local pass2 = false

	local function passfunc1()
		pass = true
	end

	local function passfunc2()
		pass2 = true
	end

	testValue.Changed:Connect(passfunc1)
	testValue.Value += 1
	wait(0)
	assert(pass == true)
	pass = false
	pass2 = false
	-- Test disconnect func1
	testValue.Changed:Disconnect(passfunc1)
	testValue.Changed:Once(passfunc2)

	testValue.Value += 1
	wait(0)
	assert(pass == false) -- passfunc1 shouldn't be called again
	assert(pass2 == true) -- Once should call

	-- Test :Once
	pass2 = false
	testValue.Value += 1
	assert(pass2 == false)
end
