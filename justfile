set shell := ["pwsh", "-NoLogo", "-NoProfile", "-Command"]

default:
    @just --list

restore:
    dotnet restore LDOCE5ViewerX.slnx

build:
    dotnet build -warnaserror LDOCE5ViewerX.slnx

test:
    dotnet test --solution LDOCE5ViewerX.slnx

format:
    dotnet format LDOCE5ViewerX.slnx
    & './scripts/Format-Repository.ps1'

generate-icons:
    & './scripts/New-AppIcons.ps1'

generate-icons-docker image='ldoce5viewerx-icon-tools':
    docker build -f './scripts/icon-tools/Dockerfile' -t '{{image}}' .
    docker run --rm --user "$(& id -u):$(& id -g)" -v "$($PWD.Path):/workspace" -w /workspace '{{image}}'

publish-package runtime archive_name:
    & './scripts/Publish-Package.ps1' -Runtime '{{runtime}}' -ArchiveName '{{archive_name}}'

publish-win-x64 archive_name='LDOCE5ViewerX-win-x64.7z':
    just publish-package win-x64 "{{archive_name}}"

publish-win-arm64 archive_name='LDOCE5ViewerX-win-arm64.7z':
    just publish-package win-arm64 "{{archive_name}}"

publish-linux-x64 archive_name='LDOCE5ViewerX-linux-x64.AppImage':
    just publish-package linux-x64 "{{archive_name}}"

publish-linux-arm64 archive_name='LDOCE5ViewerX-linux-arm64.AppImage':
    just publish-package linux-arm64 "{{archive_name}}"

publish-osx-x64 archive_name='LDOCE5ViewerX-osx-x64.7z':
    just publish-package osx-x64 "{{archive_name}}"

publish-osx-arm64 archive_name='LDOCE5ViewerX-osx-arm64.7z':
    just publish-package osx-arm64 "{{archive_name}}"
