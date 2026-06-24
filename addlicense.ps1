# Requires https://github.com/google/addlicense

Get-ChildItem -Recurse -Filter *.cs |
Where-Object {
    $_.FullName -notlike "*BrickVerse\addons\*"
} |
ForEach-Object {
    addlicense -c "BrickVerse" -l "MPL-2.0" $_.FullName
}