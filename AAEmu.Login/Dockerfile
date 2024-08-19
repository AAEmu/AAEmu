FROM mcr.microsoft.com/dotnet/sdk:8.0 AS builder
ARG CONFIGURATION
ARG RUNTIME

WORKDIR app
COPY ./Directory.Build.props .
COPY ./AAEmu.Commons ./AAEmu.Commons
COPY ./AAEmu.Login ./AAEmu.Login
RUN dotnet publish ./AAEmu.Login/AAEmu.Login.csproj -c $CONFIGURATION -r $RUNTIME --self-contained true

FROM ubuntu:20.04
ARG CONFIGURATION
ARG FRAMEWORK
ARG RUNTIME
ARG DB_HOST
ARG DB_PORT
ARG DB_USER
ARG DB_PASSWORD

RUN apt update && apt install openssl mysql-client -y
WORKDIR app
COPY --from=builder app/AAEmu.Login/bin/$CONFIGURATION/$FRAMEWORK/$RUNTIME/publish ./

EXPOSE 1234 1237
ENTRYPOINT ["./AAEmu.Login"]