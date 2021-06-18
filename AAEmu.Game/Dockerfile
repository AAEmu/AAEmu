FROM mcr.microsoft.com/dotnet/sdk:3.1.409-focal as builder
ARG CONFIGURATION
ARG RUNTIME
ARG GAME_DB_URL

RUN apt update && apt install xz-utils -y
WORKDIR app
COPY ./AAEmu.Commons ./AAEmu.Commons
COPY ./AAEmu.Game ./AAEmu.Game
RUN test -f ./AAEmu.Game/Data/compact.sqlite3 || wget "$GAME_DB_URL" -qO - | tar -xJf - -C ./AAEmu.Game/Data
RUN dotnet publish ./AAEmu.Game/AAEmu.Game.csproj -c $CONFIGURATION -r $RUNTIME --self-contained true

FROM ubuntu:20.04
ARG CONFIGURATION
ARG FRAMEWORK
ARG RUNTIME
ARG LOGIN_HOST
ARG LOGIN_PORT
ARG DB_HOST
ARG DB_PORT
ARG DB_USER
ARG DB_PASSWORD

RUN apt update && apt install openssl -y
WORKDIR app
COPY --from=builder app/AAEmu.Game/bin/$CONFIGURATION/$FRAMEWORK/$RUNTIME/publish ./
RUN cp ./ExampleConfig.json ./Config.json
RUN sed -i "s/%login_host%/$LOGIN_HOST/" ./Config.json
RUN sed -i "s/%login_port%/$LOGIN_PORT/" ./Config.json
RUN sed -i "s/%db_host%/$DB_HOST/" ./Config.json
RUN sed -i "s/%db_port%/$DB_PORT/" ./Config.json
RUN sed -i "s/%db_user%/$DB_USER/" ./Config.json
RUN sed -i "s/%db_password%/$DB_PASSWORD/" ./Config.json

EXPOSE 1239 1250
ENTRYPOINT ["./AAEmu.Game"]