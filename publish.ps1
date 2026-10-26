$ErrorActionPreference = 'Stop'

Write-Host 'Publicando SSH Tunnel Manager para Windows x64...' -ForegroundColor Cyan
dotnet publish .\SshTunnelManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o .\publish\win-x64

if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falhou.' }
Write-Host ''
Write-Host 'Concluído. Executável em: .\publish\win-x64\SshTunnelManager.exe' -ForegroundColor Green
Write-Host 'Copie a chave SSH para a pasta do executável ou configure seu caminho absoluto em Settings.'
