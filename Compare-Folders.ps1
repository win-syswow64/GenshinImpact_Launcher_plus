param(
    [Parameter(Mandatory=$true)][string]$Folder1,
    [Parameter(Mandatory=$true)][string]$Folder2
)

function Get-FileHashMap {
    param([string]$Path)
    $dict = @{}
    Get-ChildItem -Path $Path -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($Path.Length).TrimStart('\','/')
        $hash = (Get-FileHash -Path $_.FullName -Algorithm MD5).Hash
        $dict[$relative] = [PSCustomObject]@{
            RelativePath = $relative
            FullPath     = $_.FullName
            MD5          = $hash
        }
    }
    return $dict
}

$Folder1 = (Resolve-Path $Folder1).Path
$Folder2 = (Resolve-Path $Folder2).Path

Write-Host "Comparing:" -ForegroundColor Cyan
Write-Host "  Folder1: $Folder1"
Write-Host "  Folder2: $Folder2"
Write-Host ""

$map1 = Get-FileHashMap -Path $Folder1
$map2 = Get-FileHashMap -Path $Folder2

$allKeys = ($map1.Keys + $map2.Keys) | Sort-Object -Unique

$results = @()

foreach ($key in $allKeys) {
    $in1 = $map1.ContainsKey($key)
    $in2 = $map2.ContainsKey($key)

    if ($in1 -and -not $in2) {
        $results += [PSCustomObject]@{
            RelativePath = $key
            Folder1Path  = $map1[$key].FullPath
            Folder2Path  = "(missing)"
            Folder1MD5   = $map1[$key].MD5
            Folder2MD5   = "(missing)"
            Status       = "Only in Folder1"
        }
    }
    elseif (-not $in1 -and $in2) {
        $results += [PSCustomObject]@{
            RelativePath = $key
            Folder1Path  = "(missing)"
            Folder2Path  = $map2[$key].FullPath
            Folder1MD5   = "(missing)"
            Folder2MD5   = $map2[$key].MD5
            Status       = "Only in Folder2"
        }
    }
    elseif ($map1[$key].MD5 -ne $map2[$key].MD5) {
        $results += [PSCustomObject]@{
            RelativePath = $key
            Folder1Path  = $map1[$key].FullPath
            Folder2Path  = $map2[$key].FullPath
            Folder1MD5   = $map1[$key].MD5
            Folder2MD5   = $map2[$key].MD5
            Status       = "MD5 Different"
        }
    }
}

if ($results.Count -eq 0) {
    Write-Host "All files are identical." -ForegroundColor Green
}
else {
    Write-Host "Found $($results.Count) difference(s):" -ForegroundColor Yellow
    Write-Host ""
    $results | Format-Table -AutoSize -Property Status, RelativePath, Folder1MD5, Folder2MD5
}

return $results
