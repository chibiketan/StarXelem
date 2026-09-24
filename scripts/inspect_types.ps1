param(
    [Parameter(Mandatory=$true)]
    [string]$ClassRegex,

    [Parameter(Mandatory=$false)]
    [string]$TargetDll = "StarBreaker.DataCore.dll",

    [Parameter(Mandatory=$false)]
    [switch]$Json
)

# This script requires PowerShell 7+ (pwsh) to load .NET 10 assemblies
$PSVersionTable.PSVersion | Out-Null

$libsDir = Join-Path $PSScriptRoot "..\libs"
$dllPath = Join-Path $libsDir $TargetDll

if (-not (Test-Path $dllPath)) {
    Write-Error "DLL not found: $dllPath"
    exit 1
}

# Load all DLLs from libs/ to resolve dependencies
$allDlls = Get-ChildItem -Path $libsDir -Filter "*.dll" -File
$loadedCount = 0
$failedDlls = @()

foreach ($dll in $allDlls) {
    try {
        [System.Reflection.Assembly]::LoadFrom($dll.FullName) | Out-Null
        $loadedCount++
    } catch {
        $failedDlls += $dll.Name
    }
}

if (-not $Json) {
    Write-Host "Loaded $loadedCount dependency DLL(s) from libs/"
    if ($failedDlls.Count -gt 0) {
        Write-Warning "Could not load DLL(s): $($failedDlls -join ', ')"
    }
}

$assembly = [System.Reflection.Assembly]::LoadFrom($dllPath)

$failedTypes = 0
try {
    $types = $assembly.GetTypes()
} catch [System.Reflection.ReflectionTypeLoadException] {
    $types = $_.Exception.Types | Where-Object { $_ -ne $null }
    $failedTypes = $_.Exception.LoaderExceptions.Count
}

$matchedTypes = $types | Where-Object {
    $_.Name -match $ClassRegex
}

if ($matchedTypes.Count -eq 0) {
    if ($Json) {
        @{ found = @(); regex = $ClassRegex; dll = $TargetDll; total = 0 } | ConvertTo-Json -Compress
    } else {
        Write-Host "No types found matching regex: $ClassRegex"
    }
    exit 0
}

if (-not $Json -and $failedTypes -gt 0) {
    Write-Warning "$failedTypes type(s) could not be loaded (missing dependencies)"
}

if ($Json) {
    # JSON output: structured data for programmatic consumption
    $flags = [System.Reflection.BindingFlags]::Instance -bor
             [System.Reflection.BindingFlags]::Public -bor
             [System.Reflection.BindingFlags]::NonPublic -bor
             [System.Reflection.BindingFlags]::Static

    $results = @()
    foreach ($type in $matchedTypes) {
        $properties = @()
        foreach ($prop in $type.GetProperties($flags)) {
            $modifiers = @()
            if ($prop.Attributes -band [System.Reflection.PropertyAttributes]::Static) { $modifiers += "static" }
            if ($prop.DeclaringType -ne $type) { $modifiers += "inherited" }

            $properties += @{
                name       = $prop.Name
                type       = $prop.PropertyType.FullName
                typeShort  = $prop.PropertyType.Name
                canRead    = $prop.CanRead
                canWrite   = $prop.CanWrite
                modifiers  = $modifiers
            }
        }

        $methods = @()
        foreach ($method in $type.GetMethods($flags)) {
            if ($method.Name -eq "GetType" -or $method.Name -eq "ToString" -or $method.Name -eq "Equals" -or $method.Name -eq "GetHashCode") { continue }
            $params = @()
            foreach ($p in $method.GetParameters()) {
                $params += @{ name = $p.Name; type = $p.ParameterType.Name }
            }

            $mod = @()
            if ($method.IsStatic) { $mod += "static" }
            if ($method.IsVirtual) { $mod += "virtual" }
            if ($method.IsAbstract) { $mod += "abstract" }
            if ($method.DeclaringType -ne $type) { $mod += "inherited" }

            $methods += @{
                name      = $method.Name
                return    = $method.ReturnType.Name
                parameters = $params
                modifiers = $mod
            }
        }

        $fields = @()
        foreach ($field in $type.GetFields($flags)) {
            $mod = @()
            if ($field.IsStatic) { $mod += "static" }
            if ($field.IsLiteral) { $mod += "const" }
            if ($field.IsInitOnly) { $mod += "readonly" }
            if ($field.DeclaringType -ne $type) { $mod += "inherited" }

            $fields += @{
                name      = $field.Name
                type      = $field.FieldType.Name
                modifiers = $mod
            }
        }

        $interfaces = @()
        foreach ($iface in $type.GetInterfaces()) {
            $interfaces += $iface.Name
        }

        $baseType = if ($type.BaseType) { $type.BaseType.Name } else { $null }

        $results += @{
            fullName   = $type.FullName
            name       = $type.Name
            namespace  = $type.Namespace
            baseType   = $baseType
            interfaces = $interfaces
            isClass    = $type.IsClass
            isAbstract = $type.IsAbstract
            isSealed   = $type.IsSealed
            isEnum     = $type.IsEnum
            properties = $properties
            methods    = $methods
            fields     = $fields
        }
    }

    @{
        dll       = $TargetDll
        regex     = $ClassRegex
        total     = $results.Count
        failed    = $failedTypes
        types     = $results
    } | ConvertTo-Json -Depth 10 4>$null
} else {
    # Human-readable output
    Write-Host "Found $($matchedTypes.Count) type(s) matching '$ClassRegex':"
    Write-Host ("=" * 60)

    foreach ($type in $matchedTypes) {
        Write-Host ""
        Write-Host "Class: $($type.FullName)"
        Write-Host ("-" * 60)

        $flags = [System.Reflection.BindingFlags]::Instance -bor
                 [System.Reflection.BindingFlags]::Public -bor
                 [System.Reflection.BindingFlags]::NonPublic -bor
                 [System.Reflection.BindingFlags]::Static

        $properties = $type.GetProperties($flags)

        if ($properties.Count -eq 0) {
            Write-Host "  (no properties)"
            continue
        }

        foreach ($prop in $properties) {
            $accessors = @()
            if ($prop.CanRead) { $accessors += "get" }
            if ($prop.CanWrite) { $accessors += "set" }
            $accessorStr = "[$($accessors -join ', ')]"

            $modifiers = @()
            if ($prop.Attributes -band [System.Reflection.PropertyAttributes]::Static) { $modifiers += "static" }
            if ($prop.DeclaringType -ne $type) { $modifiers += "inherited" }
            $modifierStr = if ($modifiers.Count) { " ($($modifiers -join ', '))" } else { "" }

            Write-Host "  $($prop.PropertyType.Name) $($prop.Name) $accessorStr$modifierStr"
        }
    }

    Write-Host ""
    Write-Host ("=" * 60)
    Write-Host "Total: $($matchedTypes.Count) type(s)"
}
