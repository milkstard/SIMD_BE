# Regenerates contracts/swagger.json. Uses fixed placeholder identity/connection settings so the output is
# identical on every machine and in CI (the frontend generates its TypeScript types from this file).
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)

$settings = @{
    'ConnectionStrings__Sql'  = 'Server=localhost;Database=openapi;Trusted_Connection=true'
    'ConnectionStrings__Redis' = 'localhost:6379'
    'AzureAd__TenantId'       = '00000000-0000-0000-0000-000000000001'
    'AzureAd__ClientId'       = '00000000-0000-0000-0000-000000000002'
    'Authorization__GroupRoleMap__00000000-0000-0000-0000-000000000003' = 'Admin'
}

try {
    foreach ($key in $settings.Keys) { [Environment]::SetEnvironmentVariable($key, $settings[$key], 'Process') }
    dotnet tool restore
    dotnet build src/IncidentHub.Api --nologo -v q
    dotnet swagger tofile --output contracts/swagger.json src/IncidentHub.Api/bin/Debug/net8.0/IncidentHub.Api.dll v1
}
finally {
    foreach ($key in $settings.Keys) { [Environment]::SetEnvironmentVariable($key, $null, 'Process') }
}
