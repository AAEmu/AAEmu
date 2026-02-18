FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS builder
ARG CONFIGURATION

WORKDIR app
COPY ./Directory.Build.props .
COPY ./Directory.Packages.props .
COPY ./AAEmu.Commons ./AAEmu.Commons
COPY ./AAEmu.Login ./AAEmu.Login
RUN dotnet publish ./AAEmu.Login/AAEmu.Login.csproj -c $CONFIGURATION -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine

ARG CONFIGURATION
ARG DB_HOST
ARG DB_PORT
ARG DB_USER
ARG DB_PASSWORD

RUN apk add --no-cache openssl mysql-client

WORKDIR app

COPY --from=builder /app/publish ./

EXPOSE 1234 1237
ENTRYPOINT ["dotnet", "AAEmu.Login.dll"]