@echo off
setlocal enabledelayedexpansion

set "SCRIPT_DIR=%~dp0"
set "PARENT_DIR=%SCRIPT_DIR%\.."
set "SRC_DIR=%PARENT_DIR%\Src"

if "%~1"=="" goto :usage

set "action=%~1"
set "arch=%~2"

if "%action%"=="build-compile" (
    if "%arch%"=="" goto :usage
    call :Build-CompileImage %arch%
) else if "%action%"=="build-runtime" (
    call :Build-RuntimeImage
) else if "%action%"=="compile-engine" (
    call :Compile-Engine
) else if "%action%"=="compile-engine-ia" (
    call :Compile-Engine-IA
) else if "%action%"=="compile-engine-beta" (
    call :Compile-Engine-Beta
) else if "%action%"=="compile-engine-release" (
    call :Compile-Engine-Release
) else if "%action%"=="compile-clienttestconsole" (
    if "%arch%"=="" goto :usage
    call :Compile-TestClientConsole %arch%
) else if "%action%"=="compile-pf" (
    call :Compile-PathFinder
) else if "%action%"=="run-engine" (
    call :Run-Engine
) else if "%action%"=="run-engine-full" (
    call :Run-Engine-Full
) else (
    goto :usage
)

goto :eof

:Build-CompileImage
docker build --platform linux/%1 -t roof-compile-%1 -f compile_%1.dockerfile ..
goto :eof

:Build-RuntimeImage
docker build --platform linux/arm64 -t roof-runtime -f runtime.dockerfile .
goto :eof

:Compile-Engine
docker run --privileged --rm -it -v "%SRC_DIR%":/hostSrc -v "%SRC_DIR%\Roof.Engine\VM_PUBLISHED_ARTIFACTS":/VM_PUBLISHED_ARTIFACTS roof-compile-arm64 Roof.Engine/Roof.Engine.csproj -c DEBUG -r linux-arm64
goto :eof

:Compile-Engine-IA
docker run --privileged --rm -it -v "%SRC_DIR%":/hostSrc -v "%SRC_DIR%\Roof.Engine\VM_PUBLISHED_ARTIFACTS":/VM_PUBLISHED_ARTIFACTS roof-compile-arm64 Roof.Engine/Roof.Engine.csproj -c IA -r linux-arm64
goto :eof

:Compile-Engine-Beta
docker run --privileged --rm -it -v "%SRC_DIR%":/hostSrc -v "%SRC_DIR%\Roof.Engine\VM_PUBLISHED_ARTIFACTS":/VM_PUBLISHED_ARTIFACTS roof-compile-arm64 Roof.Engine/Roof.Engine.csproj -c BETA -r linux-arm64
goto :eof

:Compile-Engine-Release
docker run --privileged --rm -it -v "%SRC_DIR%":/hostSrc -v "%SRC_DIR%\Roof.Engine\VM_PUBLISHED_ARTIFACTS":/VM_PUBLISHED_ARTIFACTS roof-compile-arm64 Roof.Engine/Roof.Engine.csproj -c RELEASE -r linux-arm64
goto :eof

:Compile-TestClientConsole
docker run --privileged --rm -it -v "%SRC_DIR%":/hostSrc -v "%SRC_DIR%\Roof.ClientTestConsole\VM_PUBLISHED_ARTIFACTS":/VM_PUBLISHED_ARTIFACTS roof-compile-%1 Roof.ClientTestConsole/Roof.ClientTestConsole.csproj -r linux-%1
goto :eof

:Compile-PathFinder
docker run --privileged --rm -it -v "%SRC_DIR%":/hostSrc -v "%SRC_DIR%\Roof.PathFinder\VM_PUBLISHED_ARTIFACTS":/VM_PUBLISHED_ARTIFACTS roof-compile-arm64 Roof.PathFinder/Roof.PathFinder.csproj -r linux-arm64
goto :eof

:Compile-PathFinderBridge
docker run --privileged --rm -it -v "%SRC_DIR%":/hostSrc -v "%SRC_DIR%\Roof.PathFinderBridge\VM_PUBLISHED_ARTIFACTS":/VM_PUBLISHED_ARTIFACTS roof-compile-amd64 Roof.PathFinderBridge/Roof.PathFinderBridge.csproj -r linux-amd64
goto :eof

:Run-Engine
docker network create --driver bridge --subnet=192.168.0.0/24 --gateway=192.168.0.1 roof-vm-net
docker run --privileged --rm -it --network roof-vm-net --ip 192.168.0.253 -p 8671:8671/tcp -p 22:22/tcp -p 6969:6969/udp -p 8671:8671/udp -v "%SRC_DIR%/Roof.Engine/VM_PUBLISHED_ARTIFACTS:/roof" roof-runtime /bin/bash -c "sudo ./Roof.Engine; exec /bin/bash"
goto :eof

:Run-Engine-Full
docker network create --driver bridge --subnet=192.168.0.0/24 --gateway=192.168.0.1 roof-vm-net
docker run --privileged --rm -d --name roof-runtimeVM --network roof-vm-net --ip 192.168.0.254 -p 8671:8671/tcp -p 22:22/tcp -p 6969:6969/udp -p 8671:8671/udp -v "%SRC_DIR%/Roof.Engine/VM_PUBLISHED_ARTIFACTS":/roof roof-runtime
docker exec -it roof-runtimeVM /bin/bash -c "sudo ./Roof.Engine; exec /bin/bash"
goto :eof

:usage
echo Usage: %0 {build-compile^|build-runtime^|compile-engine^|compile-client^|compile-pf^|run-engine^|run-engine-full} [arch]
echo   build-compile: Requires arch (arm64 or amd64)
echo   build-runtime: No arch required
echo   compile-engine: No arch required (always arm64) - VERSION: DEBUG
echo   compile-engine-IA: No arch required (always arm64) - VERSION: IA
echo   compile-engine-Beta: No arch required (always arm64) - VERSION: BETA
echo   compile-engine-release: No arch required (always arm64) - VERSION: RELEASE
echo   compile-clienttestconsole: Requires arch (arm64 or amd64)
echo   compile-pf: No arch required
echo   run-engine: No arch required
echo   run-engine-full: No arch required
exit /b 1