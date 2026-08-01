FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        libasound2t64 \
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

# Fail the image build if an exported native library requires a newer glibc
# or another shared dependency that this runtime image does not provide.
RUN set -eux; \
    luau_vm="$(find . -type f -name 'Luau.VM.so' -print -quit)"; \
    test -n "$luau_vm"; \
    ldd "$luau_vm" 2>&1 | tee /tmp/luau-ldd.txt; \
    ! grep -qi 'not found' /tmp/luau-ldd.txt

USER brickverse
EXPOSE 24221/udp

# The node daemon appends: -network server -token <hostToken>
ENTRYPOINT ["./BrickVerseServer.x86_64"]
