if (!(Test-Path -Path $env:UNITY_BUILD_DIR)) {
    New-Item -ItemType Directory -Path $env:UNITY_BUILD_DIR | Out-Null
    Write-Output "Created build directory: $env:UNITY_BUILD_DIR"
} else {
    Write-Output "Build directory already exists: $env:UNITY_BUILD_DIR"
}