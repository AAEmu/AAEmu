FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS builder
ARG CONFIGURATION
ARG GAME_DB_URL

RUN apk add --no-cache xz

WORKDIR app
COPY ./Directory.Build.props .
COPY ./Directory.Packages.props .
COPY ./AAEmu.Commons ./AAEmu.Commons
COPY ./AAEmu.Game ./AAEmu.Game
RUN dotnet publish ./AAEmu.Game/AAEmu.Game.csproj -c $CONFIGURATION -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine

ARG CONFIGURATION
ARG LOGIN_HOST
ARG LOGIN_PORT
ARG DB_HOST
ARG DB_PORT
ARG DB_USER
ARG DB_PASSWORD

RUN apk add --no-cache openssl-dev mysql-client-dev || apk add --no-cache libssl3 mariadb-client || true

WORKDIR app
COPY --from=builder /app/publish ./
COPY ./SQL/patches/compact ./SQL/patches/compact

EXPOSE 1239 1250
ENTRYPOINT ["dotnet", "AAEmu.Game.dll"]