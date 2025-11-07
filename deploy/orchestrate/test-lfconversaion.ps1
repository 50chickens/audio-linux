$errorpreference = "stop"
get-childitem "$PSScriptRoot/includes" -Filter "*.ps1" | ForEach-Object {
    Write-Verbose "Sourcing $($_.FullName)..."
    . $_.FullName
}

# Example usage of Convert-LineEndingsToWindows

# 1) Convert a literal string
"first line`nsecond line" | Convert-LineEndingsToWindows

# # 2) Convert a file in-place (use -Raw to preserve original newlines), save as UTF8
# (Get-Content "C:\temp\example.txt" -Raw) |
#     Convert-LineEndingsToWindows |
#     Set-Content "C:\temp\example.txt" -Encoding UTF8

# # 3) Convert and write to a new file
# (Get-Content "C:\repo\script.sh" -Raw) |
#     Convert-LineEndingsToWindows |
#     Set-Content "C:\repo\script-windows.sh" -Encoding UTF8

# # 4) Line-by-line pipeline (Get-Content without -Raw sends each line; use when appropriate)
# Get-Content "C:\temp\multiline.txt" |
#     Convert-LineEndingsToWindows |
#     Set-Content "C:\temp\multiline-fixed.txt" -Encoding UTF8

# # 5) Use from stdin in a one-liner (PowerShell CLI)
# Get-Content -Raw unixfile.txt | Convert-LineEndingsToWindows | Set-Content windowsfile.txt -Encoding UTF8
