@echo off
color 0A

echo Sending join request...

curl.exe ^
  -X POST ^
  -H "Content-Type: application/json" ^
  -d "{\"platform\":\"PC\",\"universeId\":\"278079276731858944\",\"worldId\":\"278079276819939328\"}" ^
  "https://api.brickverse.gg/api/v3/world/join"

echo.
pause