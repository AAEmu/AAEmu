## Setup instructions

### Configuration

To configure the game server you have you can just use a **configuration file** or combine it with **user secrets (preferred)** to hide credentials from files in the repository.

The configuration structure is as follows:

```
"Id": 1,
"AdditionalesId": [],
"SecretKey": "test",
"Network": {
    "Host": "*",
    "Port": 1239,
    "NumConnections": 10
},
"StreamNetwork": {
    "Host": "*",
    "Port": 1250
},
"WebApiNetwork": {
    "Host": "*",
    "Port": 1280
},
"LoginNetwork": {
    "Host": "%login_host%",             <-- the ip address of the login server
    "Port": "%login_port%"              <-- the port of the login server
},
"Connections": {
    "MySQLProvider": {
        "Host": "%db_host%",            <-- localhost or any specific
        "Port": "%db_port%",            <-- 3306 or any specific
        "User": "%db_user%",            <-- root or any specific
        "Password": "%db_password%",    <-- password
        "Database": "aaemu_game"
    }
},
"CharacterNameRegex": "^[a-zA-Z0-9а-яА-Я]{1,18}$",
"MaxConcurencyThreadPool": 8,
"HeightMapsEnable": false
```

### Copy Configuration File

1. Copy `ExampleConfig.json` as `Config.json` in the `AAEmu.Game` directory
1. Open `Config.json` and change the configuration details as required
   **Don't provide any credentials in this file if you want to use User Secrets (see below)**

### Combining with User Secrets (preferred)

This is the preferred option as it won't expose your database credentials in the configuration file.

1. Open a command prompt in the `AAEmu.Game` directory
1. Start a user secrets session by running `dotnet user-secrets init`
1. Set the required secrets by running:

    ```
    dotnet user-secrets set "Id" "1"
    dotnet user-secrets set "SecretKey" "test"

    dotnet user-secrets set "LoginNetwork:Host" "your login server ip"
    dotnet user-secrets set "LoginNetwork:Port" "your login server port"
    dotnet user-secrets set "Connections:MySQLProvider:User" "your username"
    dotnet user-secrets set "Connections:MySQLProvider:Port" "port number"
    dotnet user-secrets set "Connections:MySQLProvider:Password" "your password"
    dotnet user-secrets set "Connections:MySQLProvider:Host" "localhost or specific ip"
    dotnet user-secrets set "Connections:MySQLProvider:Database" "aaemu_game"

    + any other configuration details you want change
    dotnet user-secrets set "Network:Host" "*"
    dotnet user-secrets set "Network:Port" "1239"
    dotnet user-secrets set "Network:NumConnections" "10"

    dotnet user-secrets set "StreamNetwork:Host" "*"
    dotnet user-secrets set "StreamNetwork:Port" "1250"

    dotnet user-secrets set "WebApiNetwork:Port" "1280"
    dotnet user-secrets set "WebApiNetwork:Host" "*"
    dotnet user-secrets set "CharacterNameRegex" "^[a-zA-Z0-9а-яА-Я]{1,18}$"
    dotnet user-secrets set "MaxConcurencyThreadPool" "8"
    dotnet user-secrets set "HeightMapsEnable" "false"
    ```

1. Check the secrets have been set by running `dotnet user-secrets list`
   Result will be like below **but with your values**:

    ```
    LoginNetwork:Port = 1234
    LoginNetwork:Host = 127.0.0.1
    Connections:MySQLProvider:User = root
    Connections:MySQLProvider:Port = 3306
    Connections:MySQLProvider:Password = yourpassword
    Connections:MySQLProvider:Host = localhost
    ```
