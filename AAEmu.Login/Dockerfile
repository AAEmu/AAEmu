FROM mcr.microsoft.com/dotnet/sdk:3.1.409-focal as builder
ARG CONFIGURATION
ARG RUNTIME

WORKDIR app
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

RUN apt update && apt install openssl -y
WORKDIR app
COPY --from=builder app/AAEmu.Login/bin/$CONFIGURATION/$FRAMEWORK/$RUNTIME/publish ./
RUN cp ./ExampleConfig.json ./Config.json
RUN sed -i "s/%db_host%/$DB_HOST/" ./Config.json
RUN sed -i "s/%db_port%/$DB_PORT/" ./Config.json
RUN sed -i "s/%db_user%/$DB_USER/" ./Config.json
RUN sed -i "s/%db_password%/$DB_PASSWORD/" ./Config.json

EXPOSE 1234 1237
ENTRYPOINT ["./AAEmu.Login"]