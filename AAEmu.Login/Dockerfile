FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS builder
ARG CONFIGURATION
ARG RUNTIME
ARG FRAMEWORK

WORKDIR app
COPY ./Directory.Build.props .
COPY ./Directory.Packages.props .
COPY ./AAEmu.Commons ./AAEmu.Commons
COPY ./AAEmu.Login ./AAEmu.Login
RUN dotnet publish ./AAEmu.Login/AAEmu.Login.csproj -c $CONFIGURATION -r $RUNTIME --self-contained true -f $FRAMEWORK

FROM mcr.microsoft.com/dotnet/runtime:9.0-alpine

ARG CONFIGURATION
ARG FRAMEWORK
ARG RUNTIME
ARG DB_HOST
ARG DB_PORT
ARG DB_USER
ARG DB_PASSWORD

RUN apk add --no-cache openssl mysql-client

WORKDIR app

COPY --from=builder app/AAEmu.Login/bin/$CONFIGURATION/$FRAMEWORK/$RUNTIME/publish ./

EXPOSE 1234 1237
ENTRYPOINT ["./AAEmu.Login"]