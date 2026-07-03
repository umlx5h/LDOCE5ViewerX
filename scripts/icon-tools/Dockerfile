FROM mcr.microsoft.com/powershell:7.5-ubuntu-24.04

RUN apt-get update \
    && apt-get install -y --no-install-recommends imagemagick librsvg2-bin \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /workspace

ENTRYPOINT ["pwsh", "-NoLogo", "-NoProfile", "-File", "/workspace/scripts/New-AppIcons.ps1"]
