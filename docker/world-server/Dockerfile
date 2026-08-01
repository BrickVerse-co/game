FROM debian:bookworm-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        libasound2 \
        libfontconfig1 \
        libgl1 \
        libxi6 \
        libxinerama1 \
        libxrandr2 \
        libxcursor1 \
        libx11-6 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /opt/brickverse
COPY . ./
RUN chmod +x BrickVerseServer.x86_64 \
    && useradd --system --uid 10001 --home /opt/brickverse brickverse \
    && chown -R brickverse:brickverse /opt/brickverse

USER brickverse
EXPOSE 24221/udp

# The node daemon appends: -network server -token <hostToken>
ENTRYPOINT ["./BrickVerseServer.x86_64"]
