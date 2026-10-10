# syntax=docker/dockerfile:1
# Multi-stage build of the Rust servers. Build one of the runtime targets:
#   docker build -f docker/Dockerfile --target login -t aaemu-login .
#   docker build -f docker/Dockerfile --target game  -t aaemu-game  .
# (docker-compose.yaml does this for you.)

# ---- build: compile both servers once (shared by the two runtime targets) --------------------
FROM rust:1-slim-trixie AS builder
# BUILD_PROFILE: release (default) or dev (debug build, faster compile)
ARG BUILD_PROFILE=release
WORKDIR /src
COPY rust/ ./rust/
# Machine-local cargo settings (e.g. a target-dir on another drive) must not leak into the image.
RUN rm -rf rust/.cargo
RUN --mount=type=cache,target=/usr/local/cargo/registry \
    --mount=type=cache,target=/src/rust/target \
    cd rust \
    && cargo build --locked --profile "${BUILD_PROFILE}" -p aaemu-login -p aaemu-game \
    && dir=$([ "${BUILD_PROFILE}" = "dev" ] && echo debug || echo "${BUILD_PROFILE}") \
    && mkdir -p /out \
    && cp "target/${dir}/aaemu-login" "target/${dir}/aaemu-game" /out/

# ---- runtime base -----------------------------------------------------------------------------
FROM debian:trixie-slim AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
# The servers search SQL/updates upwards from their executable's directory (/app -> /SQL).
COPY SQL/ /SQL/
ENV NO_COLOR=1 \
    RUST_LOG=info

# ---- login server -----------------------------------------------------------------------------
FROM runtime AS login
COPY --from=builder /out/aaemu-login /app/aaemu-login
COPY rust/crates/aaemu-login/app/Config.json rust/crates/aaemu-login/app/ExampleConfig.Local.json /app/
EXPOSE 1234 1237
# Ready once the internal (game server registration) port accepts connections.
HEALTHCHECK --interval=5s --timeout=5s --retries=20 \
    CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/1234' || exit 1
ENTRYPOINT ["/app/aaemu-login"]

# ---- game server ------------------------------------------------------------------------------
FROM runtime AS game
COPY --from=builder /out/aaemu-game /app/aaemu-game
# Config.json, Configurations/, Data/ (without compact.sqlite3, see .dockerignore),
# ClientData/readme.txt, Scripts/Commands/kits.json; compose mounts the server files over these.
COPY rust/crates/aaemu-game/app/ /app/
EXPOSE 1239 1250 1280
# Ready once the client port accepts connections (after all game data is loaded).
HEALTHCHECK --interval=5s --timeout=5s --start-period=10m --retries=20 \
    CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/1239' || exit 1
ENTRYPOINT ["/app/aaemu-game"]
