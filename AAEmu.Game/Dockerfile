FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS builder
ARG CONFIGURATION
ARG RUNTIME
ARG FRAMEWORK
ARG GAME_DB_URL

RUN apk add --no-cache xz

WORKDIR app
COPY ./Directory.Build.props .
COPY ./Directory.Packages.props .
COPY ./AAEmu.Commons ./AAEmu.Commons
COPY ./AAEmu.Game ./AAEmu.Game
RUN dotnet publish ./AAEmu.Game/AAEmu.Game.csproj -c $CONFIGURATION -r $RUNTIME --self-contained true -f $FRAMEWORK

FROM mcr.microsoft.com/dotnet/runtime:9.0-alpine

ARG CONFIGURATION
ARG FRAMEWORK
ARG RUNTIME
ARG LOGIN_HOST
ARG LOGIN_PORT
ARG DB_HOST
ARG DB_PORT
ARG DB_USER
ARG DB_PASSWORD

RUN apk add --no-cache openssl mysql-client

WORKDIR app
COPY --from=builder app/AAEmu.Game/bin/$CONFIGURATION/$FRAMEWORK/$RUNTIME/publish ./

EXPOSE 1239 1250
ENTRYPOINT ["./AAEmu.Game"]