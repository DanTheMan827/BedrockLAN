# syntax=docker/dockerfile:1
#
# Bedrock LAN advertiser (command-line app) in a container.
#
#   docker build -t bedrock-lan .
#   docker run -d --name bedrock-lan --network host -v bedrock-lan-data:/data \
#       bedrock-lan play.example.com 192.168.1.20:19133
#
# --network host is required: LAN discovery uses UDP broadcasts (19132, 7551), which Docker's bridge network
# doesn't forward. Persistent data (the NetherNet operator key) lives in /data/persist; a config file can be
# mounted at /data/servers.json instead of passing servers as arguments.

# ---- Build -------------------------------------------------------------------------------------------
# The build runs on the builder's own platform: the output is framework-dependent (no app host), so one build
# serves every target architecture and multi-arch builds don't compile .NET under emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (cached until a project file changes).
COPY src/BedrockLan/BedrockLan.csproj src/BedrockLan/
COPY src/BedrockLan.WebRtc/BedrockLan.WebRtc.csproj src/BedrockLan.WebRtc/
COPY src/BedrockLanAdvertiser/BedrockLanAdvertiser.csproj src/BedrockLanAdvertiser/
RUN dotnet restore src/BedrockLanAdvertiser/BedrockLanAdvertiser.csproj

COPY src/BedrockLan/ src/BedrockLan/
COPY src/BedrockLan.WebRtc/ src/BedrockLan.WebRtc/
COPY src/BedrockLanAdvertiser/ src/BedrockLanAdvertiser/
RUN dotnet publish src/BedrockLanAdvertiser/BedrockLanAdvertiser.csproj \
        -c Release -o /app --no-restore -p:UseAppHost=false \
    # The sample servers.json would otherwise be picked up when no servers are given.
    && rm -f /app/servers.json

# ---- Runtime -----------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime

# Working directory = data directory: ./persist and ./servers.json resolve here.
WORKDIR /data
RUN mkdir -p /data/persist && chown -R app:app /data
VOLUME ["/data"]

COPY --from=build /app /app

ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

# Unprivileged user from the base image; every port used is above 1024.
USER app

# Ports used (informational with --network host): RakNet discovery, NetherNet discovery,
# and one UDP port per server for the minimal RakNet endpoint.
EXPOSE 19132/udp 7551/udp 19140-19159/udp

# Servers (or options such as --config, --poll, --verbose) are passed as container arguments.
# With no arguments the app uses /data/servers.json if it exists, otherwise prints its usage.
ENTRYPOINT ["dotnet", "/app/BedrockLanAdvertiser.dll"]
