wait(1)
local tests: Folder = script.Parent.Tests
local successfulTests = 0
local failedTests = 0

print("=== Polytoria Datamodel Tests ===")

function RunTest(s)
	local startTime = os.clock()
	local success, msg = pcall(function()
		s.Entry()
	end)
	local elapsed = math.round((os.clock() - startTime) * 1000)

	if success then
		print("[✅] " .. s.Name .. " test successful (" .. elapsed .. "ms)")
		return true
	else
		print("[❌] " .. s.Name .. " test failed (" .. elapsed .. "ms): " .. msg)
		return false
	end
end

for _, s in tests:GetChildren() do
	if RunTest(s) then
		successfulTests += 1
	else
		failedTests += 1
	end
end

print("=== Result ===")
print(successfulTests, "success")
print(failedTests, "failed")

-- Call special quit function for DM Test mode
if quit then
	if failedTests > 0 then
		quit(1)
	else
		quit(0)
	end
end
