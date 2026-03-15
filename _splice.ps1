$srcFile   = 'Jellyfin.Plugin.UpcomingMovies\Web\discoverPage.js'
$patchFile = '_patch_fetchrec.js'
$src   = [System.IO.File]::ReadAllLines($srcFile)
$patch = [System.IO.File]::ReadAllLines($patchFile)
# Replace lines 459-557 (0-indexed 458..556) with the new simplified fetchRecommendations
$result = $src[0..457] + $patch + $src[557..($src.Length-1)]
[System.IO.File]::WriteAllLines($srcFile, $result, [System.Text.UTF8Encoding]::new($false))
Write-Host ("Spliced. New line count: " + $result.Length)
