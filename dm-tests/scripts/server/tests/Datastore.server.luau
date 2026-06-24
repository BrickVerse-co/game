function Entry()
	local data = math.random(1000, 9999)
	local dataKey = "data1"

	local ds = Datastore:GetDatastore("testds")
	ds:SetAsync(dataKey, data)
	assert(ds:GetAsync(dataKey) == data)
	ds:RemoveAsync(dataKey)
	assert(ds:GetAsync(dataKey) == nil)

	local ds2 = Datastore:GetDatastore("testds2")
	assert(ds2:GetAsync(dataKey) == nil)
end
