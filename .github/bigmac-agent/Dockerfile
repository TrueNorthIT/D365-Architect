# Sandbox image for the issue agent (.github/workflows/issue-agent.yml).
# Built on bigmac by the workflow: docker build -t omp-runner-d365-architect .github/bigmac-agent/
FROM oven/bun:1.4.3-slim

RUN apt-get update && apt-get install -y --no-install-recommends \
      git ripgrep jq ca-certificates curl socat \
 && rm -rf /var/lib/apt/lists/*

# .NET 10 SDK, to match the projects' net10.0 target. Invariant globalisation
# avoids pulling in libicu; nothing here needs culture data.
ENV DOTNET_ROOT=/usr/share/dotnet \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
RUN curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0 --install-dir "$DOTNET_ROOT"

# Pinned: unpinned, the cached layer never updates.
ARG OMP_VERSION=18.12.6
RUN bun add -g "@oh-my-pi/pi-coding-agent@${OMP_VERSION}"
ENV PATH="/root/.bun/bin:${DOTNET_ROOT}:${PATH}"

COPY models.yml config.yml /root/.omp/agent/
WORKDIR /workspace
